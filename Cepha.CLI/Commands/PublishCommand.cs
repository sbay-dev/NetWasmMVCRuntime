using System.Diagnostics;
using System.Text.Json;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class PublishCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleUI.Banner();

        string target = "folder"; // default
        string? outputDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "cf" or "cloudflare":
                    target = "cloudflare";
                    break;
                case "kit" or "edge" or "cf-kit":
                    target = "cloudflare-kit";
                    break;
                case "azure":
                    target = "azure";
                    break;
                case "-o" or "--output":
                    if (i + 1 < args.Length) outputDir = args[++i];
                    break;
            }
        }

        // ─── Interactive if no target ────────────────────────
        if (args.Length == 0)
        {
            var choice = ConsoleUI.Select("Publish target:", [
                "📁  Local folder (default)",
                "☁️   Cloudflare Pages",
                "⚡  Cloudflare Pages + CephaKit (Edge Worker)",
                "🔷  Azure Static Web Apps"
            ]);

            target = choice switch
            {
                1 => "cloudflare",
                2 => "cloudflare-kit",
                3 => "azure",
                _ => "folder"
            };
        }

        // ─── Find project ────────────────────────────────────
        var csproj = FindCsproj();
        if (csproj == null)
        {
            ConsoleUI.WriteError("No .csproj file found in current directory.");
            return 1;
        }

        var projectDir = Path.GetDirectoryName(csproj)!;
        var projectName = Path.GetFileNameWithoutExtension(csproj);

        return target switch
        {
            "cloudflare" => await PublishCloudflare(projectDir, projectName),
            "cloudflare-kit" => await PublishCloudflareKit(projectDir, projectName),
            "azure" => await PublishAzure(projectDir, projectName),
            _ => await PublishFolder(projectDir, projectName, outputDir)
        };
    }

    private static async Task<int> PublishFolder(string projectDir, string name, string? outputDir)
    {
        var pubDir = outputDir ?? Path.Combine(projectDir, "publish");
        ConsoleUI.WriteInfo($"Publishing '{name}' to {pubDir}...");

        var result = await ConsoleUI.WithSpinner("Building for production...", async () =>
        {
            return await RunProcess("dotnet",
                $"publish -c Release -o \"{pubDir}\" --nologo",
                projectDir);
        });

        if (result != 0)
        {
            ConsoleUI.WriteError("Publish failed.");
            return 1;
        }

        ConsoleUI.WriteSuccess("Published successfully!");
        Console.WriteLine();

        var wwwroot = Path.Combine(pubDir, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            ShowOutputStats(wwwroot);
            Console.WriteLine($"  Output directory: {wwwroot}");
            Console.WriteLine("  Deploy the 'wwwroot' folder to any static hosting provider.");
        }

        Console.WriteLine();
        return 0;
    }

    // ═══════════════════════════════════════════════════════════
    //  Cloudflare Pages
    // ═══════════════════════════════════════════════════════════

    private static async Task<int> PublishCloudflare(string projectDir, string name)
    {
        ConsoleUI.WriteInfo($"Publishing '{name}' to Cloudflare Pages...");

        // ── 1. Build ──
        var pubDir = Path.Combine(projectDir, "publish");
        var buildResult = await ConsoleUI.WithSpinner("Building for production...", async () =>
            await RunProcess("dotnet", $"publish -c Release -o \"{pubDir}\" --nologo", projectDir));

        if (buildResult != 0)
        {
            ConsoleUI.WriteError("Build failed.");
            return 1;
        }

        var wwwroot = Path.Combine(pubDir, "wwwroot");
        ShowOutputStats(wwwroot);

        // ── 2. Check Wrangler + Auth ──
        var authResult = await EnsureWranglerAuth(projectDir);
        if (authResult != 0) return authResult;

        // ── 4. Generate hosting config for WASM SPA ──
        ConsoleUI.WriteStep("Configuring WASM hosting...");
        GenerateCloudflareConfig(wwwroot);

        // ── 5. Deploy ──
        var cfName = SanitizeProjectName(name);
        ConsoleUI.WriteStep($"Deploying to project '{cfName}'...");
        Console.WriteLine();

        var deployResult = await RunProcessLive("npx",
            $"wrangler pages deploy \"{wwwroot}\" --project-name {cfName}",
            projectDir);

        Console.WriteLine();
        if (deployResult.exitCode == 0)
        {
            ConsoleUI.WriteSuccess("🚀 Deployed to Cloudflare Pages!");
            var realUrl = ExtractDeployUrl(deployResult.output);
            if (realUrl != null)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  {realUrl}");
                Console.ResetColor();
            }

            await OfferCustomDomain(cfName);
        }
        else
        {
            ConsoleUI.WriteError("Deployment failed.");
        }

        return deployResult.exitCode;
    }

    /// <summary>
    /// Generates _headers and _redirects for Blazor WASM SPA on Cloudflare Pages
    /// </summary>
    private static void GenerateCloudflareConfig(string wwwroot)
    {
        // _headers — Cross-Origin isolation for WASM threading + correct MIME types
        var headersPath = Path.Combine(wwwroot, "_headers");
        if (!File.Exists(headersPath))
        {
            File.WriteAllText(headersPath, """
            /*
              X-Content-Type-Options: nosniff
              Cross-Origin-Embedder-Policy: credentialless
              Cross-Origin-Opener-Policy: same-origin

            /_framework/*.wasm
              Content-Type: application/wasm

            /_framework/*.dat
              Content-Type: application/octet-stream
            """.Replace("            ", ""));
        }

        // _redirects — SPA fallback: all non-file paths serve index.html
        var redirectsPath = Path.Combine(wwwroot, "_redirects");
        if (!File.Exists(redirectsPath))
        {
            File.WriteAllText(redirectsPath, "/*  /index.html  200\n");
        }
    }

    /// <summary>
    /// Sanitizes project name for Cloudflare Pages (lowercase, alphanumeric + hyphens)
    /// </summary>
    private static string SanitizeProjectName(string name)
    {
        var sanitized = new string(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
        // Collapse multiple hyphens
        while (sanitized.Contains("--"))
            sanitized = sanitized.Replace("--", "-");
        return string.IsNullOrEmpty(sanitized) ? "cepha-app" : sanitized;
    }

    // ═══════════════════════════════════════════════════════════
    //  Cloudflare Pages + CephaKit Edge Worker
    // ═══════════════════════════════════════════════════════════

    private static async Task<int> PublishCloudflareKit(string projectDir, string name)
    {
        ConsoleUI.WriteInfo($"Publishing '{name}' to Cloudflare Pages + CephaKit Edge Worker...");

        // ── 1. Build ──
        var pubDir = Path.Combine(projectDir, "publish");
        var buildResult = await ConsoleUI.WithSpinner("Building for production...", async () =>
            await RunProcess("dotnet", $"publish -c Release -o \"{pubDir}\" --nologo", projectDir));

        if (buildResult != 0)
        {
            ConsoleUI.WriteError("Build failed.");
            return 1;
        }

        var wwwroot = Path.Combine(pubDir, "wwwroot");
        ShowOutputStats(wwwroot);

        // ── 2. Check Wrangler + Auth (shared logic) ──
        var authResult = await EnsureWranglerAuth(projectDir);
        if (authResult != 0) return authResult;

        // ── 3. Generate Edge Worker ──
        ConsoleUI.WriteStep("Generating CephaKit Edge Worker...");
        GenerateEdgeWorker(wwwroot, name);

        // ── 4. Deploy ──
        var cfName = SanitizeProjectName(name);
        ConsoleUI.WriteStep($"Deploying to project '{cfName}' with Edge Worker...");
        Console.WriteLine();

        var deployResult = await RunProcessLive("npx",
            $"wrangler pages deploy \"{wwwroot}\" --project-name {cfName}",
            projectDir);

        Console.WriteLine();
        if (deployResult.exitCode == 0)
        {
            ConsoleUI.WriteSuccess("🚀 Deployed to Cloudflare Pages + CephaKit Edge Worker!");
            var realUrl = ExtractDeployUrl(deployResult.output);
            if (realUrl != null)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  {realUrl}");
                Console.ResetColor();
            }
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  /_cepha/info  — Edge Worker diagnostics");
            Console.WriteLine($"  /health       — Health check endpoint");
            Console.ResetColor();

            await OfferCustomDomain(cfName);
        }
        else
        {
            ConsoleUI.WriteError("Deployment failed.");
        }

        return deployResult.exitCode;
    }

    /// <summary>
    /// Generates _worker.js — Cloudflare Pages Advanced Mode Worker for CephaKit edge features
    /// </summary>
    private static void GenerateEdgeWorker(string wwwroot, string projectName)
    {
        var workerPath = Path.Combine(wwwroot, "_worker.js");

        File.WriteAllText(workerPath, $$"""
        // 🧬 CephaKit Edge Worker — Cloudflare Pages Advanced Mode
        // Provides: SPA routing, WASM headers, CORS, health checks, edge diagnostics
        // Generated by Cepha CLI for project: {{projectName}}

        const STARTED = new Date().toISOString();

        export default {
          async fetch(request, env) {
            const url = new URL(request.url);
            const path = url.pathname;

            // ── CORS preflight ──
            if (request.method === 'OPTIONS') {
              return new Response(null, {
                status: 204,
                headers: corsHeaders()
              });
            }

            // ── Health check ──
            if (path === '/health') {
              return json({ status: 'healthy', edge: true, worker: 'CephaKit', ts: Date.now() });
            }

            // ── CephaKit edge info ──
            if (path === '/_cepha/info') {
              return json({
                server: 'CephaKit Edge Worker',
                project: '{{projectName}}',
                runtime: 'Cloudflare Workers',
                mode: 'edge-optimized',
                startedAt: STARTED,
                uptime: (Date.now() - new Date(STARTED).getTime()) / 1000,
                capabilities: [
                  'spa-routing',
                  'wasm-cross-origin-isolation',
                  'cors',
                  'brotli-precompression',
                  'edge-caching',
                  'health-endpoint'
                ]
              });
            }

            // ── Try static asset ──
            let response = await env.ASSETS.fetch(request);

            // ── SPA fallback: non-file paths → index.html ──
            if (response.status === 404 && !hasExtension(path)) {
              response = await env.ASSETS.fetch(
                new Request(new URL('/index.html', url.origin), request)
              );
            }

            // ── Enhance response with WASM headers ──
            return addHeaders(response, path);
          }
        };

        function hasExtension(path) {
          const last = path.split('/').pop();
          return last && last.includes('.') && !last.startsWith('.');
        }

        function corsHeaders() {
          return {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type, Authorization',
            'Access-Control-Max-Age': '86400'
          };
        }

        function addHeaders(response, path) {
          const h = new Response(response.body, response);
          // Cross-Origin isolation required for WASM threading (SharedArrayBuffer)
          h.headers.set('Cross-Origin-Embedder-Policy', 'credentialless');
          h.headers.set('Cross-Origin-Opener-Policy', 'same-origin');
          h.headers.set('X-Content-Type-Options', 'nosniff');
          h.headers.set('Access-Control-Allow-Origin', '*');

          // WASM MIME types
          if (path.endsWith('.wasm'))
            h.headers.set('Content-Type', 'application/wasm');
          if (path.endsWith('.dat') || path.endsWith('.blat'))
            h.headers.set('Content-Type', 'application/octet-stream');

          // Cache immutable framework assets
          if (path.startsWith('/_framework/'))
            h.headers.set('Cache-Control', 'public, max-age=31536000, immutable');

          return h;
        }

        function json(data) {
          return new Response(JSON.stringify(data, null, 2), {
            headers: {
              'Content-Type': 'application/json',
              'Access-Control-Allow-Origin': '*',
              'Cross-Origin-Embedder-Policy': 'credentialless',
              'Cross-Origin-Opener-Policy': 'same-origin'
            }
          });
        }
        """.Replace("        ", ""));
    }

    // ═══════════════════════════════════════════════════════════
    //  Azure Static Web Apps
    // ═══════════════════════════════════════════════════════════

    private static async Task<int> PublishAzure(string projectDir, string name)
    {
        ConsoleUI.WriteInfo($"Publishing '{name}' to Azure Static Web Apps...");

        // ── 1. Build ──
        var pubDir = Path.Combine(projectDir, "publish");
        var buildResult = await ConsoleUI.WithSpinner("Building for production...", async () =>
            await RunProcess("dotnet", $"publish -c Release -o \"{pubDir}\" --nologo", projectDir));

        if (buildResult != 0)
        {
            ConsoleUI.WriteError("Build failed.");
            return 1;
        }

        var wwwroot = Path.Combine(pubDir, "wwwroot");
        ShowOutputStats(wwwroot);

        // ── 2. Check SWA CLI ──
        ConsoleUI.WriteStep("Checking Azure SWA CLI...");
        var (swaCode, _) = await RunProcessCapture("npx", "@azure/static-web-apps-cli --version", projectDir);
        if (swaCode != 0)
        {
            ConsoleUI.WriteStep("SWA CLI not found. Installing...");
            var installResult = await RunInteractiveProcess("npm", "install -g @azure/static-web-apps-cli", projectDir);
            if (installResult != 0)
            {
                ConsoleUI.WriteError("Failed to install SWA CLI. Run: npm install -g @azure/static-web-apps-cli");
                return 1;
            }
            ConsoleUI.WriteSuccess("SWA CLI installed ✓");
        }

        // ── 3. Login ──
        ConsoleUI.WriteStep("🔐 Logging in to Azure...");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  A browser window will open for authentication.");
        Console.ResetColor();
        Console.WriteLine();

        var loginResult = await RunInteractiveProcess("npx", "swa login", projectDir);
        if (loginResult != 0)
        {
            ConsoleUI.WriteError("Azure login failed.");
            return 1;
        }

        // ── 4. Generate staticwebapp.config.json for WASM SPA ──
        ConsoleUI.WriteStep("Configuring WASM hosting...");
        GenerateAzureConfig(wwwroot);

        // ── 5. Deploy ──
        ConsoleUI.WriteStep("Deploying...");
        Console.WriteLine();

        var deployResult = await RunInteractiveProcess("npx",
            $"swa deploy \"{wwwroot}\" --env production",
            projectDir);

        Console.WriteLine();
        if (deployResult == 0)
            ConsoleUI.WriteSuccess("🚀 Deployed to Azure Static Web Apps!");
        else
            ConsoleUI.WriteError("Deployment failed.");

        return deployResult;
    }

    /// <summary>
    /// Generates staticwebapp.config.json for Blazor WASM SPA on Azure
    /// </summary>
    private static void GenerateAzureConfig(string wwwroot)
    {
        var configPath = Path.Combine(wwwroot, "staticwebapp.config.json");
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, """
            {
              "navigationFallback": {
                "rewrite": "/index.html",
                "exclude": ["/_framework/*", "/css/*", "/js/*", "*.{css,js,wasm,dll,dat,br,gz,ico,png,jpg,svg}"]
              },
              "globalHeaders": {
                "X-Content-Type-Options": "nosniff",
                "Cross-Origin-Embedder-Policy": "credentialless",
                "Cross-Origin-Opener-Policy": "same-origin"
              },
              "mimeTypes": {
                ".wasm": "application/wasm",
                ".dat": "application/octet-stream",
                ".blat": "application/octet-stream"
              }
            }
            """);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures Wrangler is installed and authenticated (shared by Cloudflare targets)
    /// </summary>
    private static async Task<int> EnsureWranglerAuth(string workingDir)
    {
        ConsoleUI.WriteStep("Checking Wrangler CLI...");
        var (wranglerCode, _) = await RunProcessCapture("npx", "wrangler --version", workingDir);
        if (wranglerCode != 0)
        {
            ConsoleUI.WriteStep("Wrangler not found. Installing...");
            var installResult = await RunInteractiveProcess("npm", "install -g wrangler", workingDir);
            if (installResult != 0)
            {
                ConsoleUI.WriteError("Failed to install Wrangler. Run manually: npm install -g wrangler");
                return 1;
            }
            ConsoleUI.WriteSuccess("Wrangler installed ✓");
        }

        ConsoleUI.WriteStep("Checking Cloudflare authentication...");
        var (whoamiCode, whoamiOut) = await RunProcessCapture("npx", "wrangler whoami", workingDir);
        var isAuthenticated = whoamiCode == 0
            && whoamiOut.Contains("You are logged in")
            && !whoamiOut.Contains("[ERROR]");

        if (!isAuthenticated)
        {
            ConsoleUI.WriteStep("🔐 Logging in to Cloudflare...");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  A browser window will open for authentication.");
            Console.ResetColor();
            Console.WriteLine();

            var loginResult = await RunInteractiveProcess("npx", "wrangler login", workingDir);
            if (loginResult != 0)
            {
                ConsoleUI.WriteError("Login cancelled or failed.");
                return 1;
            }

            var (verifyCode, verifyOut) = await RunProcessCapture("npx", "wrangler whoami", workingDir);
            if (verifyCode != 0 || !verifyOut.Contains("You are logged in"))
            {
                ConsoleUI.WriteError("Authentication could not be verified. Try: npx wrangler login");
                return 1;
            }

            ConsoleUI.WriteSuccess("Authenticated ✓");
            Console.WriteLine();
        }
        else
        {
            ConsoleUI.WriteSuccess("Authenticated ✓");
        }

        return 0;
    }

    private static void ShowOutputStats(string wwwroot)
    {
        if (!Directory.Exists(wwwroot)) return;
        var files = Directory.GetFiles(wwwroot, "*", SearchOption.AllDirectories);
        var totalSize = files.Sum(f => new FileInfo(f).Length);
        var brFiles = files.Where(f => f.EndsWith(".br")).ToArray();
        var brSize = brFiles.Sum(f => new FileInfo(f).Length);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  📊 Output: {files.Length} files, {FormatSize(totalSize)}");
        if (brFiles.Length > 0)
            Console.WriteLine($"  📦 Brotli: {brFiles.Length} files, {FormatSize(brSize)}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static string? FindCsproj()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        return files.Length == 1 ? files[0] : files.FirstOrDefault(f =>
            File.ReadAllText(f).Contains("NetWasmMvc.SDK", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Runs interactively — user sees all output (login flows, deploy progress)
    /// </summary>
    private static async Task<int> RunInteractiveProcess(string fileName, string arguments, string workingDir)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : fileName,
            Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        try
        {
            var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch { return 1; }
    }

    /// <summary>
    /// Runs silently — output captured (version checks, builds)
    /// </summary>
    private static async Task<int> RunProcess(string fileName, string arguments, string workingDir)
    {
        var (code, _) = await RunProcessCapture(fileName, arguments, workingDir);
        return code;
    }

    /// <summary>
    /// Runs silently and captures combined output (for parsing auth status, etc.)
    /// </summary>
    private static async Task<(int exitCode, string output)> RunProcessCapture(
        string fileName, string arguments, string workingDir)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : fileName,
            Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, stdout + "\n" + stderr);
        }
        catch
        {
            return (1, "");
        }
    }

    /// <summary>
    /// Runs with live output AND captures it (for deployment — user sees progress, we parse URL)
    /// </summary>
    private static async Task<(int exitCode, string output)> RunProcessLive(
        string fileName, string arguments, string workingDir)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : fileName,
            Arguments = isWindows ? $"/c {fileName} {arguments}" : arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var sb = new System.Text.StringBuilder();
            var process = Process.Start(psi)!;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                Console.WriteLine($"  {e.Data}");
                sb.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                Console.WriteLine($"  {e.Data}");
                sb.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            return (process.ExitCode, sb.ToString());
        }
        catch
        {
            return (1, "");
        }
    }

    /// <summary>
    /// Extracts the actual deployment URL from wrangler pages deploy output
    /// </summary>
    private static string? ExtractDeployUrl(string output)
    {
        // wrangler outputs lines like: "✨ Deployment complete! Take a peek over at https://abc123.project.pages.dev"
        // or "https://xxxx.project.pages.dev"
        foreach (var line in output.Split('\n'))
        {
            var idx = line.IndexOf("https://", StringComparison.Ordinal);
            if (idx < 0) continue;
            var url = line[idx..].Trim();
            // Trim trailing whitespace/punctuation
            var end = url.IndexOfAny([' ', '\r', '\t']);
            if (end > 0) url = url[..end];
            if (url.Contains(".pages.dev")) return url;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  Custom Domain
    // ═══════════════════════════════════════════════════════════

    private static async Task OfferCustomDomain(string projectName)
    {
        Console.WriteLine();
        var choice = ConsoleUI.Select("🌐 Custom domain:", [
            "Skip",
            "Connect a custom domain"
        ]);

        if (choice != 1) return;

        // Read wrangler OAuth token
        var token = ReadWranglerToken();
        if (token == null)
        {
            ConsoleUI.WriteError("Could not read Cloudflare credentials. Run: npx wrangler login");
            return;
        }

        // List user's zones (domains)
        ConsoleUI.WriteStep("Fetching your domains...");
        var zones = await CloudflareGet<ZoneResult[]>("zones?per_page=50", token);
        if (zones == null || zones.Length == 0)
        {
            ConsoleUI.WriteWarning("No domains found on your Cloudflare account.");
            ConsoleUI.WriteStep("Add a domain at https://dash.cloudflare.com → Add a site");
            return;
        }

        var zoneNames = zones.Select(z => $"🌐  {z.name}").ToArray();
        var zoneChoice = ConsoleUI.Select("Select domain:", zoneNames);
        var selectedZone = zones[zoneChoice];

        // Ask for subdomain
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  Subdomain (or press Enter for root): ");
        Console.ResetColor();
        var subdomain = Console.ReadLine()?.Trim();

        var fullDomain = string.IsNullOrEmpty(subdomain)
            ? selectedZone.name
            : $"{subdomain}.{selectedZone.name}";

        ConsoleUI.WriteStep($"Connecting {fullDomain}...");

        // 1. Add CNAME DNS record pointing to pages.dev
        var cnameTarget = $"{projectName}.pages.dev";
        var dnsBody = JsonSerializer.Serialize(new
        {
            type = "CNAME",
            name = string.IsNullOrEmpty(subdomain) ? "@" : subdomain,
            content = cnameTarget,
            proxied = true,
            ttl = 1
        });

        var dnsResult = await CloudflarePost($"zones/{selectedZone.id}/dns_records", dnsBody, token);
        if (dnsResult)
            ConsoleUI.WriteSuccess($"DNS record added: {fullDomain} → {cnameTarget}");
        else
            ConsoleUI.WriteWarning($"DNS record may already exist. Verify at Cloudflare dashboard.");

        // 2. Add custom domain to Pages project
        var accountId = selectedZone.account?.id;
        if (accountId != null)
        {
            var domainBody = JsonSerializer.Serialize(new { name = fullDomain });
            var domainResult = await CloudflarePost(
                $"accounts/{accountId}/pages/projects/{projectName}/domains", domainBody, token);
            if (domainResult)
                ConsoleUI.WriteSuccess($"Custom domain registered for Pages project.");
            else
                ConsoleUI.WriteWarning("Domain registration may need manual approval at dashboard.");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  🌐 https://{fullDomain}");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  SSL certificate will be provisioned automatically (may take a few minutes).");
        Console.ResetColor();
    }

    private static string? ReadWranglerToken()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "xdg.config", ".wrangler", "config", "default.toml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".wrangler", "config", "default.toml")
        };

        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            foreach (var line in File.ReadAllLines(p))
            {
                if (!line.StartsWith("oauth_token")) continue;
                var start = line.IndexOf('"') + 1;
                var end = line.LastIndexOf('"');
                if (start > 0 && end > start) return line[start..end];
            }
        }
        return Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
    }

    private static async Task<T?> CloudflareGet<T>(string endpoint, string token) where T : class
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            var resp = await http.GetStringAsync($"https://api.cloudflare.com/client/v4/{endpoint}");
            var doc = JsonDocument.Parse(resp);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return null;
            return JsonSerializer.Deserialize<T>(
                doc.RootElement.GetProperty("result").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static async Task<bool> CloudflarePost(string endpoint, string jsonBody, string token)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var resp = await http.PostAsync($"https://api.cloudflare.com/client/v4/{endpoint}", content);
            var body = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("success").GetBoolean();
        }
        catch { return false; }
    }

    private record ZoneResult(string id, string name, ZoneAccount? account);
    private record ZoneAccount(string id, string name);

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
