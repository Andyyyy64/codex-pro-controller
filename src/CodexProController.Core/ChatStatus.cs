using System.Text.Json;
using System.Text;

namespace CodexProController.Core;

public enum ChatState { Unknown, Idle, Working, NeedsApproval, Stopped, Interrupted, Closed }
public sealed record ChatStatus(string ThreadId, ChatState State, DateTimeOffset UpdatedAt);

public static class HookBridge
{
    public static ChatStatus? Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("session_id", out var id) || id.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("hook_event_name", out var name) || name.ValueKind != JsonValueKind.String) return null;
        // Subagent events share the parent ID; they must not mark the parent done.
        var state = name.GetString() switch
        {
            "SessionStart" => root.TryGetProperty("source", out var source) && source.GetString() == "compact" ? ChatState.Working : ChatState.Idle,
            "UserPromptSubmit" or "PreToolUse" or "PostToolUse" => ChatState.Working,
            "PermissionRequest" => ChatState.NeedsApproval,
            "Stop" => ChatState.Stopped,
            "Interrupt" => ChatState.Interrupted,
            "SessionEnd" => ChatState.Closed,
            _ => ChatState.Unknown
        };
        if (state == ChatState.Unknown || root.TryGetProperty("agent_id", out _) || root.TryGetProperty("subagent_id", out _)) return null;
        var threadId = Settings.NormalizeThreadId(id.GetString()!);
        return threadId.Length == 0 ? null : new(threadId, state, now);
    }

    public static void Store(string directory, ChatStatus status) =>
        AtomicFile.Write(Path.Combine(directory, Settings.NormalizeThreadId(status.ThreadId) + ".json"), JsonSerializer.Serialize(status, Settings.JsonOptions));

    public static ChatStatus? Read(string directory, string threadId)
    {
        threadId = Settings.NormalizeThreadId(threadId);
        if (threadId.Length == 0) return null;
        try
        {
            var path = Path.Combine(directory, threadId + ".json");
            if (!File.Exists(path)) return null;
            var status = JsonSerializer.Deserialize<ChatStatus>(File.ReadAllText(path), Settings.JsonOptions);
            return status?.ThreadId == threadId && Enum.IsDefined(status.State) ? status : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static string CreateConfig(string exePath, string eventsPath)
    {
        if (exePath.Contains('"') || eventsPath.Contains('"') || exePath.Contains('\n') || eventsPath.Contains('\n'))
            throw new ArgumentException("Invalid hook path.");
        static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
        var unixExe = exePath.Length > 3 && exePath[1] == ':' && exePath[2] == '\\'
            ? "/mnt/" + char.ToLowerInvariant(exePath[0]) + exePath[2..].Replace('\\', '/') : exePath;
        var command = $"{ShellQuote(unixExe)} --hook --events {ShellQuote(eventsPath)}";
        // Explicit streams also work with GUI hosts and Windows PowerShell 5.1.
        var windowsScript = $$"""
            $json = [Console]::In.ReadToEnd()
            $start = New-Object System.Diagnostics.ProcessStartInfo
            $start.FileName = '{{exePath.Replace("'", "''")}}'
            $start.Arguments = '--hook --events "{{eventsPath.Replace("'", "''")}}"'
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardInput = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $start
            [void]$process.Start()
            $process.StandardInput.Write($json)
            $process.StandardInput.Close()
            [Console]::Out.Write($process.StandardOutput.ReadToEnd())
            [Console]::Error.Write($process.StandardError.ReadToEnd())
            $process.WaitForExit()
            $process.Dispose()
            """;
        var commandWindows = "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(windowsScript));
        var hooks = new Dictionary<string, object>();
        foreach (var name in new[] { "SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PostToolUse", "Stop", "Interrupt", "SessionEnd" })
            hooks[name] = new[] { new { hooks = new[] { new { type = "command", command, commandWindows, timeout = 3 } } } };
        return JsonSerializer.Serialize(new { description = "Codex Pro Controller status notifications", hooks }, Settings.JsonOptions);
    }
}
