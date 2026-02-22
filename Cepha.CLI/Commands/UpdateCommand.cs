using Cepha.CLI.Services;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class UpdateCommand
{
    public static async Task<int> RunAsync()
    {
        ConsoleUI.Banner();

        // Detect current SDK version from project
        string? currentSdkVersion = null;
        var csproj = FindCsproj();
        if (csproj != null)
        {
            var content = File.ReadAllText(csproj);
            var match = System.Text.RegularExpressions.Regex.Match(content, @"Sdk=""NetWasmMvc\.SDK/([^""]+)""");
            if (match.Success)
                currentSdkVersion = match.Groups[1].Value;
        }

        ConsoleUI.WriteStep("Checking for updates...");
        Console.WriteLine();

        var (cli, sdk) = await UpdateChecker.CheckAllAsync(currentSdkVersion);

        // CLI status
        WriteUpdateRow("Cepha.CLI", cli.CurrentVersion, cli.LatestVersion, cli.UpdateAvailable);

        // SDK status
        if (currentSdkVersion != null)
            WriteUpdateRow("NetWasmMvc.SDK", sdk.CurrentVersion, sdk.LatestVersion, sdk.UpdateAvailable);
        else
            WriteUpdateRow("NetWasmMvc.SDK", "—", sdk.LatestVersion, false, "(no project found)");

        Console.WriteLine();

        // Offer actions
        if (cli.UpdateAvailable || sdk.UpdateAvailable)
        {
            var actions = new List<string>();
            if (cli.UpdateAvailable)
                actions.Add($"⬆️  Update CLI to v{cli.LatestVersion}");
            if (sdk.UpdateAvailable && currentSdkVersion != null)
                actions.Add($"⬆️  Update SDK to v{sdk.LatestVersion} in project");
            actions.Add("🚪  Skip");

            var choice = ConsoleUI.Select("Available updates:", actions.ToArray());

            if (choice == 0 && cli.UpdateAvailable)
            {
                ConsoleUI.WriteStep("Updating Cepha.CLI...");
                var result = RunProcess("dotnet", "tool update --global Cepha.CLI");
                if (result == 0)
                    ConsoleUI.WriteSuccess($"Cepha.CLI updated to v{cli.LatestVersion}");
                else
                    ConsoleUI.WriteError("Update failed. Try manually: dotnet tool update --global Cepha.CLI");
                return result;
            }
            else if ((choice == 0 && !cli.UpdateAvailable && sdk.UpdateAvailable) ||
                     (choice == 1 && cli.UpdateAvailable && sdk.UpdateAvailable))
            {
                if (csproj != null && sdk.LatestVersion != null)
                {
                    UpdateSdkVersion(csproj, currentSdkVersion!, sdk.LatestVersion);
                    ConsoleUI.WriteSuccess($"SDK updated to v{sdk.LatestVersion} in {Path.GetFileName(csproj)}");
                    ConsoleUI.WriteStep("Run 'dotnet build' to apply the new SDK.");
                }
            }
        }
        else
        {
            ConsoleUI.WriteSuccess("Everything is up to date!");
        }

        Console.WriteLine();
        return 0;
    }

    private static void WriteUpdateRow(string name, string current, string? latest, bool updateAvailable, string? note = null)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  {name,-20}");
        Console.ForegroundColor = old;
        Console.Write($"v{current}");

        if (updateAvailable && latest != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($" → v{latest}");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(" ⬆️");
        }
        else if (latest != null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(" ✅");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" (offline)");
        }

        if (note != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" {note}");
        }

        Console.ForegroundColor = old;
        Console.WriteLine();
    }

    private static void UpdateSdkVersion(string csprojPath, string oldVersion, string newVersion)
    {
        var content = File.ReadAllText(csprojPath);
        content = content.Replace($"NetWasmMvc.SDK/{oldVersion}", $"NetWasmMvc.SDK/{newVersion}");
        File.WriteAllText(csprojPath, content);
    }

    private static int RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(60_000);
            return proc?.ExitCode ?? 1;
        }
        catch { return 1; }
    }

    private static string? FindCsproj()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        return files.Length == 1 ? files[0] : files.FirstOrDefault(f =>
            File.ReadAllText(f).Contains("NetWasmMvc.SDK", StringComparison.OrdinalIgnoreCase));
    }
}
