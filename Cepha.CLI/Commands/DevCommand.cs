using System.Diagnostics;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class DevCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleUI.Banner();

        // ─── Find project ────────────────────────────────────
        var csproj = FindCsproj();
        if (csproj == null)
        {
            ConsoleUI.WriteError("No .csproj file found in current directory.");
            ConsoleUI.WriteStep("Run this command from a Cepha project directory, or use 'cepha new <name>' first.");
            return 1;
        }

        var projectDir = Path.GetDirectoryName(csproj)!;
        var projectName = Path.GetFileNameWithoutExtension(csproj);

        ConsoleUI.WriteInfo($"Starting dev server for '{projectName}'...");
        Console.WriteLine();

        // ─── Build first ─────────────────────────────────────
        ConsoleUI.WriteStep("Building project...");
        await RunProcess("dotnet", "restore --nologo -q", projectDir);
        var buildResult = await RunProcess("dotnet", "build --nologo --no-restore -v q", projectDir);
        if (buildResult != 0)
        {
            ConsoleUI.WriteError("Build failed. Fix errors and try again.");
            return 1;
        }
        ConsoleUI.WriteSuccess("Build succeeded.");

        // ─── Launch WasmAppHost (SPA dev server) ─────────────
        ConsoleUI.WriteStep("Starting SPA dev server...");
        Console.WriteLine();

        var runProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --no-build",
                WorkingDirectory = projectDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        runProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            Console.WriteLine($"  {e.Data}");
        };
        runProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            // WasmAppHost outputs URLs via stderr
            if (e.Data.Contains("http://") || e.Data.Contains("https://"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {e.Data}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  {e.Data}");
                Console.ResetColor();
            }
        };

        runProcess.Start();
        runProcess.BeginOutputReadLine();
        runProcess.BeginErrorReadLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.ResetColor();
        Console.WriteLine();

        // ─── Wait for Ctrl+C ─────────────────────────────────
        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };

        await tcs.Task;

        ConsoleUI.WriteStep("Shutting down...");
        try
        {
            if (!runProcess.HasExited)
            {
                runProcess.Kill(entireProcessTree: true);
                await runProcess.WaitForExitAsync();
            }
        }
        catch { /* process already exited */ }

        ConsoleUI.WriteSuccess("Dev server stopped.");
        return 0;
    }

    private static string? FindCsproj()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        return files.Length == 1 ? files[0] : files.FirstOrDefault(f =>
            File.ReadAllText(f).Contains("NetWasmMvc.SDK", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> RunProcess(string fileName, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(stderr);
                Console.ResetColor();
            }
            if (!string.IsNullOrWhiteSpace(stdout))
                Console.WriteLine(stdout);
        }

        return process.ExitCode;
    }
}
