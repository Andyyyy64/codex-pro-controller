using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexProController.Core;
using Binding = CodexProController.Core.Binding;

namespace CodexProController;

internal static class DesktopActions
{
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }

    public static bool IsCodexForeground() => IsCodexWindow(GetForegroundWindow());

    private static bool IsCodexWindow(nint window)
    {
        try
        {
            GetWindowThreadProcessId(window, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var path = process.MainModule?.FileName ?? "";
            return process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                path.Contains("OpenAI", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { return false; }
    }

    public static void OpenSlot(ChatSlot slot)
    {
        var id = Settings.NormalizeThreadId(slot.ThreadId);
        if (id.Length == 0) return;
        OpenUri("codex://threads/" + id);
    }
    public static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    public static void Run(Binding binding)
    {
        // Re-check at dispatch, including URI actions, so focus changes cannot leak input.
        if (!IsCodexForeground()) return;
        switch (binding.Kind)
        {
            case ActionKind.Shortcut: SendKeys.SendWait(binding.Value); break;
            case ActionKind.OpenSkills: OpenUri("codex://skills"); break;
            case ActionKind.SkillPrompt:
                OpenUri("codex://threads/new?prompt=" + Uri.EscapeDataString(binding.Value)); break;
        }
    }
    public static void Scroll(int amount)
    {
        if (!IsCodexForeground() || !GetCursorPos(out var point) || !IsCodexWindow(WindowFromPoint(point))) return;
        var input = new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { MouseData = unchecked((uint)amount), Flags = 0x0800 } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new IOException("スクロールを送信できませんでした。");
    }
}
