using System.Text.Json;

namespace CodexProController.Core;

public static class HookReceiver
{
    public static int Run(TextReader input, TextWriter output, TextWriter error, string directory)
    {
        try
        {
            var status = HookBridge.Parse(input.ReadToEnd(), DateTimeOffset.UtcNow);
            if (status is not null) HookBridge.Store(directory, status);
        }
        catch (Exception e) when (e is IOException or ArgumentException or JsonException or UnauthorizedAccessException)
        { error.WriteLine(e.Message); }
        // Valid advisory output for every event, especially Stop. Never block Codex.
        output.WriteLine("{}");
        return 0;
    }
}
