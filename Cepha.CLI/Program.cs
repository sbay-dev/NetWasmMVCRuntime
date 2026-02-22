using System.Text;
using Cepha.CLI.Commands;
using Cepha.CLI.UI;

// ─── Ensure Unicode output works on Windows ──────────────────
Console.OutputEncoding = Encoding.UTF8;

// ─── Entry Point ─────────────────────────────────────────────

if (args.Length == 0)
{
    ConsoleUI.Banner();
    InteractiveMenu.Run();
    return 0;
}

var command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

return command switch
{
    "new"       => await NewCommand.RunAsync(rest),
    "dev"       => await DevCommand.RunAsync(rest),
    "kit"       => await KitCommand.RunAsync(rest),
    "publish"   => await PublishCommand.RunAsync(rest),
    "benchmark" => await BenchmarkCommand.RunAsync(rest),
    "update"    => await UpdateCommand.RunAsync(),
    "info"      => InfoCommand.Run(),
    "help" or "--help" or "-h" => HelpCommand.Run(),
    "--version" or "-v" => VersionCommand.Run(),
    _ => UnknownCommand(command)
};

static int UnknownCommand(string cmd)
{
    ConsoleUI.WriteError($"Unknown command: {cmd}");
    Console.WriteLine();
    HelpCommand.Run();
    return 1;
}
