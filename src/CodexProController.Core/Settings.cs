using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexProController.Core;

public enum ActionKind { Shortcut, OpenSkills, SkillPrompt, None }
public sealed record Binding(string Button, ActionKind Kind, string Value);
public sealed record ChatSlot(string Name, string ThreadId);

public sealed class Settings
{
    public double DeadZone { get; set; } = 0.25;
    public bool Rumble { get; set; } = true;
    public List<ChatSlot> Slots { get; set; } = Enumerable.Range(1, 6).Select(i => new ChatSlot($"Chat {i}", "")).ToList();
    public List<Binding> Bindings { get; set; } =
    [
        new("A", ActionKind.Shortcut, "{ENTER}"), new("B", ActionKind.Shortcut, "{ESC}"),
        new("X", ActionKind.Shortcut, "^+d"), new("Y", ActionKind.Shortcut, "^+p"),
        new("L", ActionKind.Shortcut, "^+{TAB}"), new("R", ActionKind.Shortcut, "^{TAB}"),
        new("Plus", ActionKind.Shortcut, "^n"), new("Minus", ActionKind.Shortcut, "^b"),
        new("Capture", ActionKind.OpenSkills, ""), new("Home", ActionKind.Shortcut, "^%a"),
        new("Up", ActionKind.Shortcut, "{UP}"), new("Down", ActionKind.Shortcut, "{DOWN}"),
        new("Left", ActionKind.Shortcut, "{LEFT}"), new("Right", ActionKind.Shortcut, "{RIGHT}")
    ];

    public void Validate()
    {
        if (double.IsNaN(DeadZone) || DeadZone < 0.1 || DeadZone > 0.8) throw new ArgumentException("Dead zone must be 0.1–0.8.");
        if (Slots is null || Slots.Count != 6) throw new ArgumentException("Exactly six chat slots are required.");
        foreach (var slot in Slots)
        {
            if (slot is null || slot.Name is null || slot.Name.Length > 80) throw new ArgumentException("Invalid chat name.");
            _ = NormalizeThreadId(slot.ThreadId);
        }
        if (Bindings is null || Bindings.Any(b => b is null || !Enum.IsDefined(b.Kind) || b.Value is null || !Buttons.Contains(b.Button)) ||
            Bindings.Select(b => b.Button).Distinct().Count() != Bindings.Count)
            throw new ArgumentException("Invalid or duplicate button assignment.");
    }

    public static readonly string[] Buttons = ["A", "B", "X", "Y", "L", "R", "Plus", "Minus", "Home", "Capture", "Up", "Down", "Left", "Right"];
    public static string NormalizeThreadId(string value)
    {
        value = (value ?? "").Trim();
        const string prefix = "codex://threads/";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) value = value[prefix.Length..];
        if (value.Length != 0 && (value.Length > 128 || !Regex.IsMatch(value, @"\A[a-zA-Z0-9_-]+\z") || value == "new"))
            throw new ArgumentException("Paste a chat ID or codex://threads/<id> link.");
        return value;
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return new Settings();
        var result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? throw new ArgumentException("Settings are empty.");
        result.Validate();
        result.Slots = result.Slots.Select(s => s with { ThreadId = NormalizeThreadId(s.ThreadId) }).ToList();
        return result;
    }
    public void Save(string path)
    {
        Validate();
        Slots = Slots.Select(s => s with { ThreadId = NormalizeThreadId(s.ThreadId) }).ToList();
        AtomicFile.Write(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, content); File.Move(temp, path, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
