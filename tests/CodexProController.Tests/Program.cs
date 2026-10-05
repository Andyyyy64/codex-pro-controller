using System.Text.Json;
using CodexProController.Core;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}
var directory = Path.Combine(Path.GetTempPath(), "codex-pro-controller-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "config.json");
    var settings = new Settings(); settings.Save(path);
    settings.Slots[2] = new("Only slot 3 changed", "codex://threads/thread_3"); settings.Save(path);
    var restored = Settings.Load(path);
    Check(restored.Slots[2] == new ChatSlot("Only slot 3 changed", "thread_3") && restored.Slots[0] == new ChatSlot("Chat 1", ""), "single-slot save and reload");
    restored.Bindings[0] = new("A", ActionKind.None, ""); restored.Save(path);
    Check(Settings.Load(path).Bindings[0].Kind == ActionKind.None && Settings.Load(path).Slots[2].ThreadId == "thread_3", "single-binding save and reload");
    restored.DeadZone = 0.4; restored.Save(path); Check(Settings.Load(path).DeadZone == 0.4, "dead-zone save and reload");
    restored.Rumble = false; restored.Save(path); Check(!Settings.Load(path).Rumble, "rumble setting save and reload");
    Reject(() => Settings.NormalizeThreadId("../../secret"), "reject path traversal");
    Reject(() => Settings.NormalizeThreadId("https://example.com"), "reject foreign URL");
    Reject(() => Settings.NormalizeThreadId("codex://threads/new"), "reject new-chat link in fixed slot");
    restored.Slots.RemoveAt(0); Reject(restored.Validate, "reject missing slot");
    var router = new InputRouter(); var now = DateTimeOffset.UtcNow;
    ControllerInput Sample(params string[] b) => new(new HashSet<string>(b), 0);
    Check(!router.Route(Sample("A"), now, 0.25).Any(), "held button at connection does not send");
    Check(!router.Route(Sample("A"), now, 0.25).Any(), "held button does not repeat");
    _ = router.Route(Sample(), now, 0.25).ToArray();
    Check(router.Route(Sample("A"), now, 0.25).Single().Button == "A", "new press dispatches once");
    _ = router.Route(Sample(), now, 0.25).ToArray();
    var slot = router.Route(Sample("ZL", "B"), now, 0.25).Single();
    Check(slot.Slot == 1 && slot.Button is null, "slot modifier never leaks B/cancel");
    router.Reset(); Check(!router.Route(Sample("A"), now, 0.25).Any(), "rearming held button does not send");
    router.Reset(); _ = router.Route(Sample(), now, 0.25).ToArray();
    Check(!router.Route(new([], 0.2), now, 0.25).Any(), "neutral stick does not scroll");
    Check(router.Route(new([], 0.8), now, 0.25).Single().Scroll == 120, "stick scroll direction");
    Check(!router.Route(new([], 0.8), now.AddMilliseconds(30), 0.25).Any(), "scroll rate limited");
    string Hook(string name) => JsonSerializer.Serialize(new { session_id = "thread_3", hook_event_name = name, prompt = "PRIVATE TEXT NOT TO STORE" });
    foreach (var (name, state) in new[] { ("UserPromptSubmit", ChatState.Working), ("PermissionRequest", ChatState.NeedsApproval),
        ("Stop", ChatState.Stopped), ("Interrupt", ChatState.Interrupted), ("SessionEnd", ChatState.Closed) })
        Check(HookBridge.Parse(Hook(name), now)?.State == state, "hook mapping " + name);
    Check(HookBridge.Parse(Hook("SubagentStop"), now) is null, "subagent stop cannot mark parent stopped");
    Check(HookBridge.Parse("{\"session_id\":\"thread_3\",\"hook_event_name\":\"Stop\",\"agent_id\":\"agent_1\"}", now) is null, "subagent tool events ignored");
    Check(HookBridge.Parse("{}", now) is null, "missing hook fields ignored");
    Check(HookBridge.Parse("\uFEFF" + Hook("Stop"), now)?.State == ChatState.Stopped, "Windows UTF-8 BOM accepted");
    Check(HookBridge.Parse("{\"session_id\":\"thread_3\",\"hook_event_name\":\"SessionStart\",\"source\":\"compact\"}", now)?.State == ChatState.Working, "compaction restart stays working");
    var status = HookBridge.Parse(Hook("PermissionRequest"), now)!;
    HookBridge.Store(directory, status);
    Check(HookBridge.Read(directory, "thread_3") == status, "status round trip");
    Check(!File.ReadAllText(Path.Combine(directory, "thread_3.json")).Contains("PRIVATE"), "prompt text never persisted");
    File.WriteAllText(Path.Combine(directory, "thread_3.json"), "partial write");
    Check(HookBridge.Read(directory, "thread_3") is null, "corrupt status never treated as completion");
    Check(HookBridge.Read(directory, "unconfigured") is null, "missing status remains unknown");
    using var config = JsonDocument.Parse(HookBridge.CreateConfig("C:\\apps\\controller.exe", "C:\\events"));
    Check(config.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0].GetProperty("timeout").GetInt32() == 3, "valid hook config with bounded timeout");
    var handler = config.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0];
    Check(handler.GetProperty("command").GetString()!.StartsWith("'/mnt/c/apps/controller.exe'"), "WSL executable path generated");
    var encoded = handler.GetProperty("commandWindows").GetString()!.Split(' ').Last();
    var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    Check(script.Contains("$start.RedirectStandardInput = $true") && script.Contains("$process.StandardInput.Close()"), "Windows hook explicitly closes stdin");
    var output = new StringWriter(); var error = new StringWriter();
    HookReceiver.Run(new StringReader("invalid-json"), output, error, directory);
    Check(output.ToString().Trim() == "{}", "invalid hook input still returns valid advisory JSON");
    Check(!string.IsNullOrEmpty(error.ToString()), "invalid hook input reports diagnostic on stderr");
    Console.WriteLine($"{checks} checks passed.");
}
finally { Directory.Delete(directory, true); }
