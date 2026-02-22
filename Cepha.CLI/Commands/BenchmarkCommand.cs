using System.Diagnostics;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class BenchmarkCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleUI.Banner();

        // ─── Check if inside a Cepha project ─────────────────
        var csproj = FindCephaCsproj();
        string benchDir;
        bool useTempDir;

        if (csproj != null)
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            ConsoleUI.WriteInfo($"Running benchmark for '{projectName}'...");
            benchDir = Path.GetDirectoryName(csproj)!;
            useTempDir = false;
        }
        else
        {
            // No project found — scaffold a temp benchmark project
            ConsoleUI.WriteInfo("No Cepha project found. Creating temporary benchmark app...");
            benchDir = Path.Combine(Path.GetTempPath(), $"cepha-bench-{Guid.NewGuid():N}");
            Directory.CreateDirectory(benchDir);
            useTempDir = true;

            await ConsoleUI.WithSpinner("Scaffolding benchmark...", async () =>
            {
                ScaffoldBenchmarkProject(benchDir, "CephaBenchmark");
                await Task.CompletedTask;
            });
            ConsoleUI.WriteSuccess("Benchmark project ready.");
        }

        Console.WriteLine();

        // ─── Build ───────────────────────────────────────────
        ConsoleUI.WriteStep("Building...");
        var buildResult = await RunProcess("dotnet", "build --nologo -v q", benchDir);
        if (buildResult != 0)
        {
            ConsoleUI.WriteError("Build failed.");
            if (useTempDir) Cleanup(benchDir);
            return 1;
        }
        ConsoleUI.WriteSuccess("Build succeeded.");

        // ─── Run ─────────────────────────────────────────────
        ConsoleUI.WriteStep("Starting benchmark server...");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ⚡ Cepha Benchmark — High-Load Performance Testing");
        Console.ResetColor();
        Console.WriteLine();

        var runProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run --no-build",
                WorkingDirectory = benchDir,
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
            if (e.Data.Contains("http://") || e.Data.Contains("https://"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  {e.Data}");
                Console.ResetColor();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  Test pages:");
                Console.WriteLine("    /                  — Dashboard");
                Console.WriteLine("    /benchmark/stress  — 🔥 Stress Test (nodes, mitosis, spring physics)");
                Console.WriteLine("    /benchmark/frames  — 🎬 Frame Pipeline (raw DOM throughput)");
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

        if (useTempDir) Cleanup(benchDir);

        ConsoleUI.WriteSuccess("Benchmark stopped.");
        return 0;
    }

    // ─── Scaffold a standalone benchmark project ─────────────
    private static void ScaffoldBenchmarkProject(string dir, string name)
    {
        NewCommand.ScaffoldBenchmarkProject(dir, name);
    }

    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort cleanup */ }
    }

    private static string? FindCephaCsproj()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        // Only match if the project references NetWasmMvc.SDK and has benchmark controller
        return files.FirstOrDefault(f =>
        {
            var content = File.ReadAllText(f);
            if (!content.Contains("NetWasmMvc.SDK", StringComparison.OrdinalIgnoreCase))
                return false;
            // Check if project has a BenchmarkController
            var controllersDir = Path.Combine(Path.GetDirectoryName(f)!, "Controllers");
            return File.Exists(Path.Combine(controllersDir, "BenchmarkController.cs"));
        });
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
