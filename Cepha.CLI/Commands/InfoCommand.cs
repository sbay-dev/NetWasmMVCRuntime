using Cepha.CLI.Services;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class InfoCommand
{
    public static int Run()
    {
        ConsoleUI.Banner();

        var csproj = FindCsproj();
        if (csproj == null)
        {
            ConsoleUI.WriteError("No .csproj file found in current directory.");
            ConsoleUI.WriteStep("Run this command from a Cepha project directory.");
            return 1;
        }

        var projectDir = Path.GetDirectoryName(csproj)!;
        var projectName = Path.GetFileNameWithoutExtension(csproj);
        var content = File.ReadAllText(csproj);

        ConsoleUI.WriteInfo($"Project: {projectName}");
        Console.WriteLine();

        // SDK version
        string? sdkVersion = null;
        var sdkMatch = System.Text.RegularExpressions.Regex.Match(content, @"Sdk=""NetWasmMvc\.SDK/([^""]+)""");
        if (sdkMatch.Success)
        {
            sdkVersion = sdkMatch.Groups[1].Value;
            WriteRow("SDK Version", sdkVersion);
        }
        else if (content.Contains("NetWasmMvc.SDK"))
            WriteRow("SDK", "NetWasmMvc.SDK");
        else
            WriteRow("SDK", "Unknown");

        // Check for Identity
        var hasIdentity = content.Contains("WasmMvcRuntime.Identity") ||
                         content.Contains("Identity");
        WriteRow("Identity", hasIdentity ? "✅ Enabled" : "❌ Not configured");

        // Check CephaKit
        var hasCephaKit = content.Contains("CephaKitEnabled") &&
                         content.Contains("true");
        WriteRow("CephaKit", hasCephaKit ? "✅ Enabled" : "❌ Disabled");

        // Controllers
        var controllersDir = Path.Combine(projectDir, "Controllers");
        if (Directory.Exists(controllersDir))
        {
            var controllers = Directory.GetFiles(controllersDir, "*Controller.cs");
            WriteRow("Controllers", $"{controllers.Length} found");
        }

        // Views
        var viewsDir = Path.Combine(projectDir, "Views");
        if (Directory.Exists(viewsDir))
        {
            var views = Directory.GetFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories);
            WriteRow("Views", $"{views.Length} found");
        }

        // Build output
        var binDir = Path.Combine(projectDir, "bin", "Debug", "net10.0", "wwwroot");
        WriteRow("Built", Directory.Exists(binDir) ? "✅ Yes" : "❌ No (run 'dotnet build')");

        // Publish output
        var pubDir = Path.Combine(projectDir, "publish", "wwwroot");
        WriteRow("Published", Directory.Exists(pubDir) ? "✅ Yes" : "❌ No");

        Console.WriteLine();

        // Check SDK update (non-blocking, 3s timeout)
        if (sdkVersion != null)
        {
            try
            {
                var task = UpdateChecker.CheckSdkAsync(sdkVersion);
                if (task.Wait(TimeSpan.FromSeconds(3)) && task.Result.UpdateAvailable)
                {
                    var sdk = task.Result;
                    var old = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⬆️  SDK update available: v{sdkVersion} → v{sdk.LatestVersion}");
                    Console.ForegroundColor = old;
                    Console.WriteLine();

                    var actions = new[]
                    {
                        $"⬆️  Update SDK to v{sdk.LatestVersion}",
                        "🚪  Skip"
                    };
                    var choice = ConsoleUI.Select("Update SDK?", actions);
                    if (choice == 0 && sdk.LatestVersion != null)
                    {
                        var fileContent = File.ReadAllText(csproj);
                        fileContent = fileContent.Replace($"NetWasmMvc.SDK/{sdkVersion}", $"NetWasmMvc.SDK/{sdk.LatestVersion}");
                        File.WriteAllText(csproj, fileContent);
                        ConsoleUI.WriteSuccess($"SDK updated to v{sdk.LatestVersion}");
                        ConsoleUI.WriteStep("Run 'dotnet build' to apply the update.");
                    }
                }
            }
            catch { }
        }

        Console.WriteLine();
        return 0;
    }

    private static void WriteRow(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  {label,-16}");
        Console.ResetColor();
        Console.WriteLine(value);
    }

    private static string? FindCsproj()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        return files.Length == 1 ? files[0] : files.FirstOrDefault(f =>
            File.ReadAllText(f).Contains("NetWasmMvc.SDK", StringComparison.OrdinalIgnoreCase));
    }
}
