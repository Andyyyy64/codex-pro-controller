using CodexProController.Core;
using HidSharp;
using wtf.cluster.JoyCon;
using wtf.cluster.JoyCon.Calibration;
using wtf.cluster.JoyCon.HomeLed;
using wtf.cluster.JoyCon.InputReports;

namespace CodexProController;

public sealed class ControllerDevice : IDisposable
{
    private JoyCon? device;
    private CalibrationData? calibration;
    private volatile bool disposed;
    private readonly SemaphoreSlim outputLock = new(1, 1);
    public event Action<ControllerInput>? Input;
    public event Action<string>? Disconnected;
    public long ReportCount { get; private set; }
    public bool Connected => device is not null;
    public string Battery { get; private set; } = "Unknown";

    public async Task ConnectAsync()
    {
        if (device is not null) return;
        var hid = DeviceList.Local.GetHidDevices(0x057e, 0x2009).FirstOrDefault()
            ?? throw new IOException("Pro Controllerが見つかりません。Bluetooth接続を確認してください。");
        var joycon = new JoyCon(hid);
        joycon.ReportReceived += (_, report) =>
        {
            if (report is InputFull full)
            {
                ReportCount++;
                Battery = full.Battery.ToString();
                var b = full.Buttons;
                var pressed = new HashSet<string>();
                foreach (var (name, value) in new (string, bool)[] {
                    ("A",b.A),("B",b.B),("X",b.X),("Y",b.Y),("L",b.L),("R",b.R),("ZL",b.ZL),("ZR",b.ZR),
                    ("Plus",b.Plus),("Minus",b.Minus),("Home",b.Home),("Capture",b.Capture),
                    ("Up",b.Up),("Down",b.Down),("Left",b.Left),("Right",b.Right) })
                    if (value) pressed.Add(name);
                // Until factory calibration arrives, scrolling stays neutral.
                double scrollY = 0;
                if (calibration?.LeftStickCalibration is { } cal)
                    scrollY = full.LeftStick.GetCalibrated(cal, 0).Y;
                Input?.Invoke(new(pressed, scrollY));
            }
            return Task.CompletedTask;
        };
        joycon.StoppedOnError += (_, error) => { if (!disposed) Disconnected?.Invoke(error.Message); return Task.CompletedTask; };
        try
        {
            joycon.Start();
            await joycon.SetInputReportModeAsync(JoyCon.InputReportType.Full);
            calibration = await joycon.GetFactoryCalibrationAsync();
            await joycon.EnableRumbleAsync(true);
            device = joycon;
        }
        catch { disposed = true; joycon.Dispose(); throw; }
    }

    public async Task FeedbackAsync(int slot, ChatState state, bool pulse)
    {
        await outputLock.WaitAsync();
        try
        {
            var current = device;
            if (current is null) return;
            var bits = state == ChatState.Unknown ? 0 : Math.Clamp(slot + 1, 1, 6);
            JoyCon.LedState Led(int bit) => state == ChatState.NeedsApproval ? JoyCon.LedState.Blinking :
                (bits & (1 << bit)) != 0 ? JoyCon.LedState.On : JoyCon.LedState.Off;
            var fourth = state switch { ChatState.Working => JoyCon.LedState.Blinking, ChatState.NeedsApproval => JoyCon.LedState.Blinking,
                ChatState.Stopped => JoyCon.LedState.On, _ => JoyCon.LedState.Off };
            await current.SetPlayerLedsAsync(Led(0), Led(1), Led(2), fourth);
            await current.SetHomeLedDimmingPatternAsync(new HomeLedDimmingPattern
            {
                StepDurationBase = 8, StartLedBrightness = 0, FullCyclesNumber = 0,
                HomeLedDimmingSteps = {
                    new HomeLedDimmingStep { LedBrightness = state is ChatState.Working or ChatState.NeedsApproval ? (byte)8 : (byte)0, TransitionDuration = 4, PauseDuration = 4 },
                    new HomeLedDimmingStep { LedBrightness = 0, TransitionDuration = 4, PauseDuration = 4 }
                }
            });
            if (pulse)
            {
                try { await current.WriteRumble(160, 0.2); await Task.Delay(130); }
                finally { await current.WriteRumble(160, 0); }
            }
        }
        finally { outputLock.Release(); }
    }

    public async Task<string[]> ReadPlayerLightsAsync()
    {
        await outputLock.WaitAsync();
        try
        {
            if (device is null) throw new IOException("Controller is not connected.");
            return (await device.GetPlayerLedsAsync()).Select(led => led.ToString()).ToArray();
        }
        finally { outputLock.Release(); }
    }

    public void Dispose()
    {
        // UI awaits output work before disposing. Never write SPI or pairing state.
        disposed = true;
        device?.Dispose(); device = null; calibration = null;
    }
}
