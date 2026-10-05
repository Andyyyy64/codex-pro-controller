using CodexProController.Core;

var index = Array.IndexOf(args, "--events");
var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] :
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexProController", "events");
return HookReceiver.Run(Console.In, Console.Out, Console.Error, directory);
