using System.Diagnostics;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class KitCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleUI.Banner();

        bool useWrangler = args.Any(a => a is "--wrangler" or "-w");
        int port = 3001;

        // Parse port
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--port" or "-p" && i + 1 < args.Length)
                int.TryParse(args[i + 1], out port);
        }

        // ─── Find project ────────────────────────────────────
        var csproj = FindCsproj();
        if (csproj == null)
        {
            ConsoleUI.WriteError("No .csproj file found in current directory.");
            return 1;
        }

        var projectDir = Path.GetDirectoryName(csproj)!;

        // ─── Ensure EnableCephaKit() in Program.cs ───────────
        EnsureCephaKit(projectDir);

        // ─── Build project ───────────────────────────────────
        ConsoleUI.WriteStep("Building project...");
        // Always restore first to ensure packages are available
        await RunProcess("dotnet", "restore --nologo -q", projectDir);
        var buildResult = await RunProcess("dotnet", "build --nologo --no-restore -q", projectDir);
        if (buildResult != 0)
        {
            ConsoleUI.WriteError("Build failed. Fix errors and try again.");
            return 1;
        }
        ConsoleUI.WriteSuccess("Build succeeded.");

        // ─── Locate cepha-server.mjs ─────────────────────────
        var serverScript = FindServerScript(projectDir);
        if (serverScript == null)
        {
            ConsoleUI.WriteError("cepha-server.mjs not found.");
            return 1;
        }

        if (useWrangler)
        {
            return await RunWithWrangler(projectDir, port);
        }

        return await RunWithNode(serverScript, port);
    }

    private static async Task<int> RunWithNode(string serverScript, int port)
    {
        ConsoleUI.WriteInfo($"Starting CephaKit on port {port} (Node.js)...");
        Console.WriteLine();

        // ─── Export dev cert ─────────────────────────────────
        var certDir = Path.GetDirectoryName(serverScript)!;
        var certPath = Path.Combine(certDir, "cepha-dev.pem");
        var keyPath = Path.Combine(certDir, "cepha-dev.key");

        if (!File.Exists(certPath))
        {
            ConsoleUI.WriteStep("Exporting dev certificate...");
            var certResult = await RunProcess("dotnet",
                $"dev-certs https --export-path \"{certPath}\" --format PEM --no-password",
                certDir);
            if (certResult == 0)
                ConsoleUI.WriteSuccess("Dev certificate exported.");
            else
                ConsoleUI.WriteWarning("Could not export dev cert. CephaKit will use HTTP.");
        }

        // ─── Start Node server ───────────────────────────────
        var env = new Dictionary<string, string>
        {
            ["PORT"] = port.ToString(),
            ["CEPHA_CERT"] = certPath,
            ["CEPHA_KEY"] = keyPath
        };

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{serverScript}\"",
            WorkingDirectory = Path.GetDirectoryName(serverScript)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var (k, v) in env)
            psi.EnvironmentVariables[k] = v;

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"  {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"  {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  🔌 CephaKit: https://localhost:{port}");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.ResetColor();
        Console.WriteLine();

        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
        await tcs.Task;

        ConsoleUI.WriteStep("Shutting down CephaKit...");
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch { }

        ConsoleUI.WriteSuccess("CephaKit stopped.");
        return 0;
    }

    private static async Task<int> RunWithWrangler(string projectDir, int port)
    {
        ConsoleUI.WriteInfo($"Starting CephaKit via Wrangler on port {port}...");

        // Check wrangler installed
        var checkResult = await RunProcess("npx", "wrangler --version", projectDir);
        if (checkResult != 0)
        {
            ConsoleUI.WriteError("Wrangler not found. Install with: npm install -g wrangler");
            return 1;
        }

        // Find publish output
        var publishDir = Path.Combine(projectDir, "bin", "Release", "net10.0", "publish", "wwwroot");
        if (!Directory.Exists(publishDir))
        {
            ConsoleUI.WriteWarning("Publish output not found. Building for production...");
            var pubResult = await RunProcess("dotnet", "publish -c Release", projectDir);
            if (pubResult != 0)
            {
                ConsoleUI.WriteError("Publish failed.");
                return 1;
            }
        }

        // Run wrangler pages dev
        var psi = new ProcessStartInfo
        {
            FileName = "npx",
            Arguments = $"wrangler pages dev \"{publishDir}\" --port {port}",
            WorkingDirectory = projectDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"  {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"  {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.ResetColor();

        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
        await tcs.Task;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch { }

        ConsoleUI.WriteSuccess("Wrangler stopped.");
        return 0;
    }

    private static void EnsureCephaKit(string projectDir)
    {
        var programCs = Path.Combine(projectDir, "Program.cs");
        if (!File.Exists(programCs)) return;

        var content = File.ReadAllText(programCs);
        if (content.Contains("EnableCephaKit", StringComparison.Ordinal)) return;

        // Insert app.EnableCephaKit(); before await app.RunAsync()
        var runIdx = content.IndexOf("await app.RunAsync()", StringComparison.Ordinal);
        if (runIdx < 0)
            runIdx = content.IndexOf("app.RunAsync()", StringComparison.Ordinal);

        if (runIdx >= 0)
        {
            // Detect file line ending style
            var eol = content.Contains("\r\n") ? "\r\n" : "\n";

            // Find indentation of the matched line
            var lineStart = content.LastIndexOf('\n', runIdx);
            var indent = lineStart >= 0 ? content[(lineStart + 1)..runIdx].Replace("\r", "") : "";

            content = content.Insert(runIdx, $"app.EnableCephaKit();{eol}{indent}");
            File.WriteAllText(programCs, content);
            ConsoleUI.WriteSuccess("Added app.EnableCephaKit() to Program.cs");
        }
        else
        {
            ConsoleUI.WriteWarning("Could not find app.RunAsync() in Program.cs. Add app.EnableCephaKit() manually.");
        }
    }

    private static string? FindCsproj()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        return files.Length == 1 ? files[0] : files.FirstOrDefault(f =>
            File.ReadAllText(f).Contains("NetWasmMvc.SDK", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindServerScript(string projectDir)
    {
        // Check wwwroot first (source)
        var src = Path.Combine(projectDir, "wwwroot", "cepha-server.mjs");
        if (File.Exists(src)) return src;

        // Check build output
        var bin = Path.Combine(projectDir, "bin", "Debug", "net10.0", "wwwroot", "cepha-server.mjs");
        if (File.Exists(bin)) return bin;

        return null;
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
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch
        {
            return 1;
        }
    }
}
