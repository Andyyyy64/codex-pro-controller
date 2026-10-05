namespace CodexProController.Core;

public sealed record ControllerInput(HashSet<string> Buttons, double ScrollY);
public sealed record RoutedInput(string? Button = null, int? Slot = null, int Scroll = 0);

// First sample after connecting/arming is a baseline, so held buttons cannot fire.
public sealed class InputRouter
{
    private HashSet<string>? previous;
    private DateTimeOffset lastScroll;
    public void Reset() => previous = null;
    public IEnumerable<RoutedInput> Route(ControllerInput input, DateTimeOffset now, double deadZone)
    {
        if (previous is null) { previous = new(input.Buttons); yield break; }
        var pressed = input.Buttons.Except(previous).ToArray();
        previous = new(input.Buttons);
        foreach (var button in pressed)
        {
            if (input.Buttons.Contains("ZL"))
            {
                var index = Array.IndexOf(new[] { "A", "B", "X", "Y", "L", "R" }, button);
                if (index >= 0) yield return new(Slot: index);
            }
            else if (Settings.Buttons.Contains(button)) yield return new(Button: button);
        }
        if (!input.Buttons.Contains("ZL") && Math.Abs(input.ScrollY) > deadZone && now - lastScroll >= TimeSpan.FromMilliseconds(100))
        {
            lastScroll = now;
            yield return new(Scroll: input.ScrollY > 0 ? 120 : -120);
        }
    }
}
