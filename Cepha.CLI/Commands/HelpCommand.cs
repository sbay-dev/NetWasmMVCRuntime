using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class HelpCommand
{
    public static int Run()
    {
        ConsoleUI.Banner();

        Console.WriteLine("  Usage: cepha [command] [options]");
        Console.WriteLine();
        Console.WriteLine("  Commands:");
        Console.WriteLine();

        WriteCmd("new <name>",         "Create a new Cepha MVC application");
        WriteCmd("new <name> --identity", "Create with Identity authentication");
        WriteCmd("dev",                "Start development server (SPA + live reload)");
        WriteCmd("kit",                "Start CephaKit backend server");
        WriteCmd("kit --wrangler",     "Start via Cloudflare Wrangler (optional)");
        WriteCmd("publish",            "Build and publish for production");
        WriteCmd("publish cf",         "Publish to Cloudflare Pages");
        WriteCmd("publish azure",      "Publish to Azure Static Web Apps");
        WriteCmd("info",               "Show current project information");
        WriteCmd("benchmark",          "Run high-load UI performance tests");
        WriteCmd("update",             "Check for CLI & SDK updates");
        WriteCmd("help",               "Show this help message");

        Console.WriteLine();
        Console.WriteLine("  Options:");
        Console.WriteLine();

        WriteCmd("--version, -v",      "Show CLI version");
        WriteCmd("--help, -h",         "Show help");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Run 'cepha' without arguments for interactive mode.");
        Console.ResetColor();
        Console.WriteLine();

        return 0;
    }

    private static void WriteCmd(string cmd, string desc)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"    {cmd,-28}");
        Console.ResetColor();
        Console.WriteLine(desc);
    }
}

internal static class VersionCommand
{
    public static int Run()
    {
        var version = typeof(VersionCommand).Assembly.GetName().Version;
        Console.WriteLine($"cepha {version?.ToString(3) ?? "0.0.0"}");
        return 0;
    }
}
