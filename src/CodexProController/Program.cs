using System.Text.Json;
using CodexProController.Core;

namespace CodexProController;

internal static class Program
{
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexProController");
    internal static string EventsDirectory => Path.Combine(DataDirectory, "events");
    internal static string HookExecutable => Path.Combine(AppContext.BaseDirectory, "CodexProController.Hooks.exe");

    [STAThread]
    private static int Main(string[] args)
    {
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (args.Contains("--export-hooks"))
        {
            AtomicFile.Write(Arg("--out") ?? Path.Combine(DataDirectory, "codex-pro-controller.hooks.json"),
                HookBridge.CreateConfig(HookExecutable, Arg("--events") ?? EventsDirectory));
            return 0;
        }
        if (args.Contains("--hook"))
        {
            return HookReceiver.Run(Console.In, Console.Out, Console.Error, Arg("--events") ?? EventsDirectory);
        }
        if (args.Contains("--probe"))
        {
            var output = Arg("--out") ?? Path.Combine(DataDirectory, "probe.json");
            try
            {
                using var device = new ControllerDevice();
                ControllerInput? last = null;
                device.Input += input => last = input;
                device.ConnectAsync().GetAwaiter().GetResult();
                Task.Delay(2000).GetAwaiter().GetResult();
                var feedback = args.Contains("--feedback");
                string[]? playerLights = null;
                if (feedback)
                {
                    try
                    {
                        device.FeedbackAsync(0, ChatState.Working, true).GetAwaiter().GetResult();
                        playerLights = device.ReadPlayerLightsAsync().GetAwaiter().GetResult();
                        Task.Delay(1000).GetAwaiter().GetResult();
                    }
                    finally { device.FeedbackAsync(0, ChatState.Unknown, false).GetAwaiter().GetResult(); }
                }
                AtomicFile.Write(output, JsonSerializer.Serialize(new { ok = device.ReportCount > 0, reports = device.ReportCount, battery = device.Battery,
                    buttons = last?.Buttons, scrollY = last?.ScrollY, feedbackAcknowledged = feedback, playerLights,
                    restoredLights = feedback ? device.ReadPlayerLightsAsync().GetAwaiter().GetResult() : null }, Settings.JsonOptions));
                return device.ReportCount > 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                AtomicFile.Write(output, JsonSerializer.Serialize(new { ok = false, error = e.Message }, Settings.JsonOptions)); return 1;
            }
        }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Local\\CodexProController", out var created);
        if (!created) { MessageBox.Show("Codex Pro Controllerはすでに起動しています。"); return 0; }
        Application.Run(new MainForm());
        return 0;
    }
}
