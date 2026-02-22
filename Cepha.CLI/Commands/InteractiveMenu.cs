using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class InteractiveMenu
{
    public static void Run()
    {
        while (true)
        {
            var choice = ShowMainMenu();

            switch (choice)
            {
                case 0: // New
                    RunNewSubMenu();
                    break;
                case 1: // Dev
                    ExecuteCommand("Dev Server", () => DevCommand.RunAsync([]));
                    break;
                case 2: // Kit
                    RunKitSubMenu();
                    break;
                case 3: // Publish
                    RunPublishSubMenu();
                    break;
                case 4: // Info
                    ExecuteCommand("Project Info", () => Task.FromResult(InfoCommand.Run()));
                    break;
                case 5: // Benchmark
                    ExecuteCommand("Benchmark", () => BenchmarkCommand.RunAsync([]));
                    break;
                case 6: // Update
                    ExecuteCommand("Update", () => UpdateCommand.RunAsync());
                    break;
                case 7: // Help
                    HelpCommand.Run();
                    WaitForKey();
                    break;
                case 8: // Exit
                    ConsoleUI.WriteInfo("Goodbye! 👋");
                    Console.WriteLine();
                    return;
                default: // Escape
                    ConsoleUI.WriteInfo("Goodbye! 👋");
                    Console.WriteLine();
                    return;
            }
        }
    }

    // ─── Main Menu ───────────────────────────────────────────

    private static int ShowMainMenu()
    {
        Console.Clear();
        ConsoleUI.Banner();

        var options = new[]
        {
            "🆕  New Project       — Create a new Cepha MVC app",
            "🚀  Dev Server        — Start development server",
            "🔌  CephaKit          — Start CephaKit backend",
            "📦  Publish           — Build & deploy for production",
            "ℹ️   Info              — Show project info",
            "📈  Benchmark         — Run performance tests",
            "🔄  Update            — Check for CLI & SDK updates",
            "❓  Help              — Show all commands",
            "🚪  Exit              — Quit Cepha CLI"
        };

        return ConsoleUI.Select("Main Menu — Select a command:", options);
    }

    // ─── Sub-Menus ───────────────────────────────────────────

    private static void RunNewSubMenu()
    {
        while (true)
        {
            Console.Clear();
            ConsoleUI.Banner();

            var options = new[]
            {
                "🧬  Standard MVC App          — Basic Cepha project",
                "🔐  MVC App with Identity      — Includes authentication",
                "📈  Benchmark Project           — Performance testing project",
                "🔙  Back to Main Menu"
            };

            var choice = ConsoleUI.Select("New Project — Select template:", options);

            switch (choice)
            {
                case 0:
                    ExecuteCommand("New Project", () => NewCommand.RunAsync([]));
                    return;
                case 1:
                    ExecuteCommand("New Project (Identity)", () => NewCommand.RunAsync(["--identity"]));
                    return;
                case 2:
                    ExecuteCommand("New Benchmark Project", () => NewCommand.RunAsync(["--benchmark"]));
                    return;
                default: // Back or Escape
                    return;
            }
        }
    }

    private static void RunPublishSubMenu()
    {
        while (true)
        {
            Console.Clear();
            ConsoleUI.Banner();

            var options = new[]
            {
                "📁  Local Folder               — Build to publish folder",
                "☁️   Cloudflare Pages            — Deploy to Cloudflare",
                "🔷  Azure Static Web Apps       — Deploy to Azure",
                "🔙  Back to Main Menu"
            };

            var choice = ConsoleUI.Select("Publish — Select target:", options);

            switch (choice)
            {
                case 0:
                    ExecuteCommand("Publish (Local)", () => PublishCommand.RunAsync([]));
                    return;
                case 1:
                    ExecuteCommand("Publish (Cloudflare)", () => PublishCommand.RunAsync(["cf"]));
                    return;
                case 2:
                    ExecuteCommand("Publish (Azure)", () => PublishCommand.RunAsync(["azure"]));
                    return;
                default: // Back or Escape
                    return;
            }
        }
    }

    private static void RunKitSubMenu()
    {
        while (true)
        {
            Console.Clear();
            ConsoleUI.Banner();

            var options = new[]
            {
                "🔌  Standard Mode              — Node.js dev server",
                "⚡  Wrangler Mode              — Cloudflare Wrangler",
                "🔙  Back to Main Menu"
            };

            var choice = ConsoleUI.Select("CephaKit — Select mode:", options);

            switch (choice)
            {
                case 0:
                    ExecuteCommand("CephaKit", () => KitCommand.RunAsync([]));
                    return;
                case 1:
                    ExecuteCommand("CephaKit (Wrangler)", () => KitCommand.RunAsync(["--wrangler"]));
                    return;
                default: // Back or Escape
                    return;
            }
        }
    }

    // ─── Command Execution ───────────────────────────────────

    private static void ExecuteCommand(string label, Func<Task<int>> action)
    {
        Console.Clear();
        ConsoleUI.Banner();
        ConsoleUI.WriteInfo($"Running: {label}");
        Console.WriteLine();

        try
        {
            var result = action().GetAwaiter().GetResult();

            Console.WriteLine();
            if (result == 0)
                ConsoleUI.WriteSuccess($"{label} completed successfully.");
            else
                ConsoleUI.WriteWarning($"{label} finished with exit code {result}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            ConsoleUI.WriteError($"{label} failed: {ex.Message}");
        }

        WaitForKey();
    }

    private static void WaitForKey()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Press any key to return to the main menu...");
        Console.ResetColor();
        Console.ReadKey(true);
        Console.WriteLine();
    }
}
