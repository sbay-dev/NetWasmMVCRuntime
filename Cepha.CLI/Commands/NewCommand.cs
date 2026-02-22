using System.Reflection;
using Cepha.CLI.UI;

namespace Cepha.CLI.Commands;

internal static class NewCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleUI.Banner();

        // Resolve latest SDK version from NuGet in background
        var sdkVersionTask = ResolveSdkVersionAsync();

        // ─── Parse arguments ─────────────────────────────────
        string? projectName = null;
        bool withIdentity = false;
        bool withBenchmark = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--identity" or "-i":
                    withIdentity = true;
                    break;
                case "--benchmark" or "-b":
                    withBenchmark = true;
                    break;
                default:
                    if (!args[i].StartsWith('-'))
                        projectName = args[i];
                    break;
            }
        }

        // ─── Interactive if no name provided ─────────────────
        if (string.IsNullOrEmpty(projectName))
        {
            projectName = ConsoleUI.Prompt("Project name", "MyCephaApp");
            if (string.IsNullOrEmpty(projectName))
            {
                ConsoleUI.WriteError("Project name is required.");
                return 1;
            }

            if (!withIdentity && !withBenchmark)
            {
                var templateChoice = ConsoleUI.Select("Select template:", [
                    "🧬  Cepha MVC (default)",
                    "🔐  Cepha MVC + Identity (authentication)",
                    "⚡  Cepha Benchmark (UI stress tests)"
                ]);
                withIdentity = templateChoice == 1;
                withBenchmark = templateChoice == 2;
            }
        }

        // ─── Validate name ───────────────────────────────────
        if (!IsValidProjectName(projectName))
        {
            ConsoleUI.WriteError($"Invalid project name: '{projectName}'. Use letters, digits, dots, hyphens, or underscores.");
            return 1;
        }

        var targetDir = Path.Combine(Directory.GetCurrentDirectory(), projectName);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            ConsoleUI.WriteError($"Directory '{projectName}' already exists and is not empty.");
            return 1;
        }

        // ─── Scaffold ────────────────────────────────────────
        ConsoleUI.WriteInfo($"Creating '{projectName}'...");

        // Ensure SDK version is resolved before scaffolding
        await sdkVersionTask;

        await ConsoleUI.WithSpinner("Scaffolding project...", async () =>
        {
            Directory.CreateDirectory(targetDir);
            if (withBenchmark)
                ScaffoldBenchmarkProject(targetDir, projectName);
            else
                ScaffoldProject(targetDir, projectName, withIdentity);
            await Task.CompletedTask;
        });

        ConsoleUI.WriteSuccess($"Project '{projectName}' created!");
        Console.WriteLine();

        // ─── Next steps ──────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Next steps:");
        Console.ResetColor();
        Console.WriteLine($"    cd {projectName}");
        Console.WriteLine("    dotnet build");
        Console.WriteLine("    cepha dev");
        Console.WriteLine();

        return 0;
    }

    private static bool IsValidProjectName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

    // ─── Scaffold all project files ──────────────────────────

    private static void ScaffoldProject(string dir, string name, bool identity)
    {
        // .csproj
        WriteFile(dir, $"{name}.csproj", GenerateCsproj(name, identity));

        // Program.cs
        WriteFile(dir, "Program.cs", GenerateProgram(identity));

        // Controllers
        var controllersDir = Path.Combine(dir, "Controllers");
        Directory.CreateDirectory(controllersDir);
        WriteFile(controllersDir, "HomeController.cs", GenerateHomeController(name));

        if (identity)
        {
            // Areas/Identity/Controllers
            var areaControllersDir = Path.Combine(dir, "Areas", "Identity", "Controllers");
            Directory.CreateDirectory(areaControllersDir);
            WriteFile(areaControllersDir, "AccountController.cs", GenerateAccountController(name));

            // Areas/Identity/Views/Account
            var areaViewsDir = Path.Combine(dir, "Areas", "Identity", "Views");
            var areaViewsAccount = Path.Combine(areaViewsDir, "Account");
            Directory.CreateDirectory(areaViewsAccount);
            WriteFile(areaViewsDir, "_ViewStart.cshtml", """
@{
    Layout = "_Layout";
}
""");
            WriteFile(areaViewsAccount, "Login.cshtml", GenerateLoginView());
            WriteFile(areaViewsAccount, "Register.cshtml", GenerateRegisterView());
        }

        // Views
        var viewsDir = Path.Combine(dir, "Views");
        var viewsHome = Path.Combine(viewsDir, "Home");
        var viewsShared = Path.Combine(viewsDir, "Shared");
        Directory.CreateDirectory(viewsHome);
        Directory.CreateDirectory(viewsShared);

        WriteFile(viewsDir, "_ViewStart.cshtml", """
@{
    Layout = "_Layout";
}
""");
        WriteFile(viewsShared, "_Layout.cshtml", GenerateLayout(name, identity));
        if (identity) WriteFile(viewsShared, "_LoginPartial.cshtml", GenerateLoginPartial());
        WriteFile(viewsHome, "Index.cshtml", GenerateIndexView(name));
        WriteFile(viewsHome, "Privacy.cshtml", GeneratePrivacyView());

        // wwwroot
        var wwwroot = Path.Combine(dir, "wwwroot");
        var cssDir = Path.Combine(wwwroot, "css");
        Directory.CreateDirectory(cssDir);

        WriteFile(cssDir, "app.css", "/* Project-specific styles — add your customizations here */\n");
        WriteEmbeddedFile(cssDir, "cepha.css");
        WriteFile(wwwroot, "index.html", GenerateIndexHtml(name));
        WriteFile(wwwroot, "favicon.ico", ""); // placeholder
        WriteEmbeddedFile(wwwroot, "main.js");
        WriteEmbeddedFile(wwwroot, "cepha-runtime-worker.js");
        WriteEmbeddedFile(wwwroot, "cepha-data-worker.js");

        // PWA
        WriteFile(wwwroot, "manifest.json", GenerateManifest(name));
        WriteFile(wwwroot, "service-worker.js", GenerateServiceWorker());

        // Properties
        var propsDir = Path.Combine(dir, "Properties");
        Directory.CreateDirectory(propsDir);
        WriteFile(propsDir, "launchSettings.json", GenerateLaunchSettings(name));

        // AI instructions (.github/copilot-instructions.md)
        var githubDir = Path.Combine(dir, ".github");
        Directory.CreateDirectory(githubDir);
        WriteEmbeddedFile(githubDir, "copilot-instructions.md");
    }

    private static void WriteFile(string dir, string name, string content)
    {
        // Normalize line endings to OS default (CRLF on Windows)
        content = content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        File.WriteAllText(Path.Combine(dir, name), content);
    }

    private static void WriteEmbeddedFile(string dir, string fileName)
    {
        WriteEmbeddedFile(dir, fileName, fileName);
    }

    private static void WriteEmbeddedFile(string dir, string fileName, string resourcePath)
    {
        var asm = Assembly.GetExecutingAssembly();
        var suffix = ".Templates." + resourcePath.Replace('/', '.').Replace('\\', '.');
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resName == null)
            throw new InvalidOperationException($"Embedded resource '{resourcePath}' not found.");
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var reader = new StreamReader(stream);
        File.WriteAllText(Path.Combine(dir, fileName), reader.ReadToEnd());
    }

    // ═══ Template Generators ═════════════════════════════════

    private const string FallbackSdkVersion = "1.0.6";

    private static string GenerateCsproj(string name, bool identity)
    {
        return $$"""
<Project Sdk="NetWasmMvc.SDK/{{SdkVersion}}">

  <PropertyGroup>
    <RootNamespace>{{name}}</RootNamespace>
    <AssemblyName>{{name}}</AssemblyName>
  </PropertyGroup>

</Project>
""";
    }

    /// <summary>
    /// Resolves the latest SDK version from NuGet, falling back to the hardcoded version.
    /// Called once at the start of RunAsync so the network call doesn't block template generation.
    /// </summary>
    private static string SdkVersion = FallbackSdkVersion;

    private static async Task ResolveSdkVersionAsync()
    {
        try
        {
            var latest = await Services.UpdateChecker.GetLatestVersionAsync("NetWasmMvc.SDK");
            if (!string.IsNullOrEmpty(latest))
                SdkVersion = latest;
        }
        catch { /* use fallback */ }
    }

    private static string GenerateProgram(bool identity)
    {
        if (identity)
        {
            return """
// 🧬 Cepha Application with Identity
using System.Runtime.Versioning;
using WasmMvcRuntime.Identity.Extensions;
[assembly: SupportedOSPlatform("browser")]

var app = CephaApp.Create(services =>
{
    services.AddWasmIdentity();
});

await app.RunAsync();
""";
        }

        return """
// 🧬 Cepha Application
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("browser")]

var app = CephaApp.Create();

await app.RunAsync();
""";
    }

    private static string GenerateHomeController(string name) => $$"""
using WasmMvcRuntime.Abstractions;

namespace {{name}}.Controllers;

[Route("/")]
[Route("/home")]
[Route("/home/index")]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        ViewBag.Title = "Home";
        ViewBag.Message = "Welcome to {{name}}! 🧬";
        return View();
    }

    [Route("/home/privacy")]
    public IActionResult Privacy()
    {
        ViewBag.Title = "Privacy";
        return View();
    }
}
""";

    private static string GenerateAccountController(string name) => $$"""
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.Identity.Services;
using WasmMvcRuntime.Identity.Models;

namespace {{name}}.Areas.Identity.Controllers;

[Area("Identity")]
public class AccountController : Controller
{
    private readonly ISignInManager _signInManager;
    private readonly IUserManager _userManager;

    public AccountController(ISignInManager signInManager, IUserManager userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public IActionResult Login()
    {
        ViewBag.Title = "Sign In";
        return View();
    }

    [HttpPost]
    [Route("/identity/account/login/post")]
    public async Task<IActionResult> LoginPost()
    {
        var email = Form["email"] ?? "";
        var password = Form["password"] ?? "";

        var result = await _signInManager.PasswordSignInAsync(email, password);
        if (result.Succeeded)
            return Redirect("/");

        ViewBag.Title = "Sign In";
        ViewBag.Error = result.ErrorMessage ?? "Invalid email or password.";
        return View("Login");
    }

    public IActionResult Register()
    {
        ViewBag.Title = "Register";
        return View();
    }

    [HttpPost]
    [Route("/identity/account/register/post")]
    public async Task<IActionResult> RegisterPost()
    {
        var email = Form["email"] ?? "";
        var password = Form["password"] ?? "";
        var confirmPassword = Form["confirmPassword"] ?? "";

        if (password != confirmPassword)
        {
            ViewBag.Title = "Register";
            ViewBag.Error = "Passwords do not match.";
            return View("Register");
        }

        var result = await _userManager.CreateAsync(
            new User { Email = email, UserName = email },
            password);

        if (result.Succeeded)
            return Redirect("/identity/account/login");

        ViewBag.Error = string.Join(", ", result.Errors);
        ViewBag.Title = "Register";
        return View("Register");
    }

    [Route("/identity/account/logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Redirect("/");
    }
}
""";

    private static string GenerateLayout(string name, bool identity)
    {
        var authNav = identity
            ? "            @await Html.PartialAsync(\"_LoginPartial\")\n"
            : "";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewBag.Title — {{name}}</title>
    <link rel="stylesheet" href="css/cepha.css" />
    <link rel="stylesheet" href="css/app.css" />
</head>
<body>
    <nav class="cepha-nav">
        <div class="cepha-nav-brand">
            <a href="/">🧬 {{name}}</a>
        </div>
        <div class="cepha-nav-links">
            <a href="/">Home</a>
            <a href="/home/privacy">Privacy</a>
{{authNav}}        </div>
    </nav>

    <main class="cepha-main">
        @RenderBody()
    </main>

    <footer class="cepha-footer">
        <p>© 2026 — {{name}} · Powered by <a href="https://www.nuget.org/packages/NetWasmMvc.SDK">Cepha</a></p>
    </footer>
</body>
</html>
""";
    }

    private static string GenerateLoginPartial() => """
<cepha-auth-widget>
    @if (ViewBag.IsGuest != null)
    {
    <div class="cepha-auth-guest">
        <a class="cepha-auth-btn cepha-auth-btn-outline" href="/identity/account/login">Sign In</a>
        <a class="cepha-auth-btn cepha-auth-btn-primary" href="/identity/account/register">Register</a>
    </div>
    }
    @if (ViewBag.UserName != null)
    {
    <div class="cepha-auth-user">
        <span class="cepha-auth-avatar">👤</span>
        <span class="cepha-auth-name">@ViewBag.UserName</span>
        <a class="cepha-auth-btn cepha-auth-btn-outline" href="/identity/account/logout">Sign Out</a>
    </div>
    }
    <style>
        cepha-auth-widget{display:contents}
        .cepha-auth-guest,.cepha-auth-user{display:flex;align-items:center;gap:.5rem}
        .cepha-auth-btn{all:unset;cursor:pointer;padding:.35rem .85rem;border-radius:6px;font-size:.85rem;font-weight:500;transition:all .2s ease;text-decoration:none;white-space:nowrap}
        .cepha-auth-btn-outline{border:1px solid var(--cepha-border,#e0e0e0);color:var(--cepha-text,#333)}
        .cepha-auth-btn-outline:hover{background:var(--cepha-surface,#f5f5f5);border-color:var(--cepha-primary,#6200ea)}
        .cepha-auth-btn-primary{background:var(--cepha-primary,#6200ea);color:#fff;border:1px solid transparent}
        .cepha-auth-btn-primary:hover{background:var(--cepha-primary-dark,#3700b3)}
        .cepha-auth-avatar{display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:50%;background:var(--cepha-primary,#6200ea);color:#fff;font-size:.9rem}
        .cepha-auth-name{font-size:.85rem;font-weight:600;color:var(--cepha-text,#333)}
    </style>
</cepha-auth-widget>
""";

    private static string GenerateIndexView(string name) => $$"""
<div class="cepha-hero">
    <h1>@ViewBag.Message</h1>
    <p class="lead">Running entirely in WebAssembly — no server required! 🧬</p>
    <p class="text-muted">Built with <strong>NetWasmMvc.SDK</strong> — ASP.NET MVC in the browser.</p>
</div>

<div class="cepha-features">
    <div class="cepha-card">
        <h3>⚡ Full MVC</h3>
        <p>Controllers, Views, Routing — the ASP.NET MVC pattern you know, running in WASM.</p>
    </div>
    <div class="cepha-card">
        <h3>🗄️ SQLite</h3>
        <p>Entity Framework Core with SQLite — a real database in your browser.</p>
    </div>
    <div class="cepha-card">
        <h3>📡 SignalR</h3>
        <p>Real-time communication between browser tabs with in-process SignalR hubs.</p>
    </div>
</div>
""";

    private static string GeneratePrivacyView() => """
<h1>@ViewBag.Title</h1>
<p>Your privacy is important. This application runs entirely in your browser — no data is sent to any server.</p>
""";

    private static string GenerateLoginView() => """
<div class="cepha-auth-form">
    <h2>Sign In</h2>
    @if (ViewBag.Error != null)
    {
        <div class="cepha-alert cepha-alert-error">@ViewBag.Error</div>
    }
    <form action="/identity/account/login/post" method="post">
        <div class="cepha-field">
            <label for="email">Email</label>
            <input type="email" id="email" name="email" required />
        </div>
        <div class="cepha-field">
            <label for="password">Password</label>
            <input type="password" id="password" name="password" required />
        </div>
        <button type="submit" class="cepha-btn cepha-btn-primary">Sign In</button>
    </form>
    <p class="cepha-auth-link">Don't have an account? <a href="/identity/account/register">Register</a></p>
</div>
""";

    private static string GenerateRegisterView() => """
<div class="cepha-auth-form">
    <h2>Create Account</h2>
    @if (ViewBag.Error != null)
    {
        <div class="cepha-alert cepha-alert-error">@ViewBag.Error</div>
    }
    <form action="/identity/account/register/post" method="post">
        <div class="cepha-field">
            <label for="email">Email</label>
            <input type="email" id="email" name="email" required />
        </div>
        <div class="cepha-field">
            <label for="password">Password</label>
            <input type="password" id="password" name="password" required minlength="6" />
        </div>
        <div class="cepha-field">
            <label for="confirmPassword">Confirm Password</label>
            <input type="password" id="confirmPassword" name="confirmPassword" required minlength="6" />
        </div>
        <button type="submit" class="cepha-btn cepha-btn-primary">Register</button>
    </form>
    <p class="cepha-auth-link">Already have an account? <a href="/identity/account/login">Sign In</a></p>
</div>
""";

    private static string GenerateLaunchSettings(string name) => $$"""
{
  "profiles": {
    "{{name}}": {
      "commandName": "Project",
      "launchBrowser": true,
      "inspectUri": "{wsProtocol}://{url.hostname}:{url.port}/_framework/debug/ws-proxy?browser={browserInspectUri}",
      "applicationUrl": "https://127.0.0.1:0;http://127.0.0.1:0",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
""";

    private static string GenerateIndexHtml(string name) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="theme-color" content="#667eea" />
    <title>{{name}} — Powered by Cepha</title>
    <base href="/" />
    <link rel="manifest" href="manifest.json" />
    <link rel="stylesheet" href="css/cepha.css" />
    <link rel="stylesheet" href="css/app.css" />
    <link rel="icon" href="favicon.ico" />
    <script type="importmap"></script>
    <script type="module">import "./main.js";</script>
</head>
<body>
    <div id="app">
        <div style="display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;background:#0f0f23;">
            <div style="font-size:4rem;">🧬</div>
            <h2 style="color:#667eea;">Loading...</h2>
        </div>
    </div>
    <script>navigator.serviceWorker?.register('service-worker.js');</script>
</body>
</html>
""";

    private static string GenerateManifest(string name) => $$"""
{
  "name": "{{name}}",
  "short_name": "{{name}}",
  "description": "{{name}} — .NET MVC in WebAssembly",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#0b0e17",
  "theme_color": "#667eea",
  "orientation": "any",
  "icons": [
    { "src": "favicon.ico", "sizes": "64x64", "type": "image/x-icon" }
  ],
  "categories": ["productivity"]
}
""";

    private static string GenerateServiceWorker() => """
// 🧬 Cepha PWA Service Worker
// Network-first for HTML/JS (ensures fresh fingerprints + import maps)
// Cache-first for immutable fingerprinted assets (*.{hash}.ext)
const CACHE_NAME = 'cepha-v2';

self.addEventListener('install', e => {
    self.skipWaiting();
});

self.addEventListener('activate', e => {
    e.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// Fingerprinted file pattern: name.{10+ char hash}.ext
const FINGERPRINTED = /\.\w{10,}\.\w+$/;

self.addEventListener('fetch', e => {
    const url = new URL(e.request.url);
    if (url.origin !== location.origin || e.request.method !== 'GET') return;

    // Fingerprinted assets are immutable — cache-first
    if (FINGERPRINTED.test(url.pathname)) {
        e.respondWith(
            caches.match(e.request).then(cached => {
                if (cached) return cached;
                return fetch(e.request).then(res => {
                    if (res.ok) {
                        const clone = res.clone();
                        caches.open(CACHE_NAME).then(c => c.put(e.request, clone));
                    }
                    return res;
                });
            })
        );
        return;
    }

    // Everything else (HTML, non-fingerprinted JS, CSS) — network-first
    e.respondWith(
        fetch(e.request).then(res => {
            if (res.ok) {
                const clone = res.clone();
                caches.open(CACHE_NAME).then(c => c.put(e.request, clone));
            }
            return res;
        }).catch(() => caches.match(e.request))
    );
});
""";

    // ═══ Benchmark Project Scaffolding ═══════════════════════

    internal static void ScaffoldBenchmarkProject(string dir, string name)
    {
        WriteFile(dir, $"{name}.csproj", GenerateCsproj(name, false));
        WriteFile(dir, "Program.cs", GenerateBenchmarkProgram());

        // Controllers
        var controllersDir = Path.Combine(dir, "Controllers");
        Directory.CreateDirectory(controllersDir);
        WriteFile(controllersDir, "BenchmarkController.cs", GenerateBenchmarkController(name));

        // Views
        var viewsDir = Path.Combine(dir, "Views");
        var viewsBench = Path.Combine(viewsDir, "Benchmark");
        var viewsShared = Path.Combine(viewsDir, "Shared");
        Directory.CreateDirectory(viewsBench);
        Directory.CreateDirectory(viewsShared);

        WriteFile(viewsDir, "_ViewStart.cshtml", "@{\n    Layout = \"_Layout\";\n}\n");
        WriteFile(viewsShared, "_Layout.cshtml", GenerateBenchmarkLayout(name));

        // Embedded benchmark views
        WriteEmbeddedFile(viewsBench, "Index.cshtml", "Benchmark/Index.cshtml");
        WriteFile(viewsBench, "Stress.cshtml", GenerateBenchmarkStressView());
        WriteFile(viewsBench, "Frames.cshtml", GenerateBenchmarkFramesView());
        WriteEmbeddedFile(viewsBench, "ClickStorm.cshtml", "Benchmark/ClickStorm.cshtml");
        WriteEmbeddedFile(viewsBench, "Physics.cshtml", "Benchmark/Physics.cshtml");
        WriteEmbeddedFile(viewsBench, "WebGL.cshtml", "Benchmark/WebGL.cshtml");
        WriteEmbeddedFile(viewsBench, "DataSiege.cshtml", "Benchmark/DataSiege.cshtml");
        WriteEmbeddedFile(viewsBench, "CryptoMatryoshka.cshtml", "Benchmark/CryptoMatryoshka.cshtml");
        WriteEmbeddedFile(viewsBench, "TunnelBreach.cshtml", "Benchmark/TunnelBreach.cshtml");

        // Framework comparison views (React / Vue / Vanilla)
        string[] fxTests = ["ClickStorm", "Physics", "WebGL", "DataSiege", "CryptoMatryoshka", "TunnelBreach"];
        string[] fxSuffixes = ["React", "Vue", "Vanilla"];
        foreach (var test in fxTests)
            foreach (var fx in fxSuffixes)
                WriteEmbeddedFile(viewsBench, $"{test}{fx}.cshtml", $"Benchmark/{test}{fx}.cshtml");

        // wwwroot
        var wwwroot = Path.Combine(dir, "wwwroot");
        var cssDir = Path.Combine(wwwroot, "css");
        Directory.CreateDirectory(cssDir);

        WriteFile(cssDir, "app.css", "/* Benchmark styles */\n");
        WriteEmbeddedFile(cssDir, "cepha.css");
        WriteFile(wwwroot, "index.html", GenerateIndexHtml(name));
        WriteFile(wwwroot, "favicon.ico", "");
        WriteEmbeddedFile(wwwroot, "main.js");
        WriteEmbeddedFile(wwwroot, "cepha-runtime-worker.js");
        WriteEmbeddedFile(wwwroot, "cepha-data-worker.js");
        WriteFile(wwwroot, "manifest.json", GenerateManifest(name));
        WriteFile(wwwroot, "service-worker.js", GenerateServiceWorker());

        var propsDir = Path.Combine(dir, "Properties");
        Directory.CreateDirectory(propsDir);
        WriteFile(propsDir, "launchSettings.json", GenerateLaunchSettings(name));

        // AI instructions (.github/copilot-instructions.md)
        var githubDir = Path.Combine(dir, ".github");
        Directory.CreateDirectory(githubDir);
        WriteEmbeddedFile(githubDir, "copilot-instructions.md");
    }

    private static string GenerateBenchmarkProgram()=> """
// ⚡ Cepha Benchmark Application
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("browser")]

var app = CephaApp.Create();
await app.RunAsync("/benchmark/index");
""";

    private static string GenerateBenchmarkController(string name) => $$"""
using WasmMvcRuntime.Abstractions;

namespace {{name}}.Controllers;

public class BenchmarkController : Controller
{
    [Route("/")]
    [Route("/benchmark")]
    [Route("/benchmark/index")]
    public IActionResult Index()
    {
        ViewBag.Title = "⚡ Benchmark Suite";
        return View();
    }

    [Route("/benchmark/stress")]
    public IActionResult Stress()
    {
        ViewBag.Title = "🔥 Stress Test";
        ViewBag.WorkerTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        ViewBag.WorkerFrequency = System.Diagnostics.Stopwatch.Frequency;
        return View();
    }

    [Route("/benchmark/frames")]
    public IActionResult Frames()
    {
        ViewBag.Title = "🎬 Frame Pipeline";
        ViewBag.WorkerTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        ViewBag.WorkerFrequency = System.Diagnostics.Stopwatch.Frequency;
        return View();
    }

    [Route("/benchmark/clicks")]
    public IActionResult ClickStorm() { ViewBag.Title = "🎯 Click Storm"; return View(); }

    [Route("/benchmark/clicks-react")]
    public IActionResult ClickStormReact() { ViewBag.Title = "⚛️ Click Storm — React"; return View(); }

    [Route("/benchmark/clicks-vue")]
    public IActionResult ClickStormVue() { ViewBag.Title = "🟢 Click Storm — Vue"; return View(); }

    [Route("/benchmark/clicks-vanilla")]
    public IActionResult ClickStormVanilla() { ViewBag.Title = "🟠 Click Storm — Vanilla"; return View(); }

    [Route("/benchmark/physics")]
    public IActionResult Physics() { ViewBag.Title = "🌌 Particle Physics"; return View(); }

    [Route("/benchmark/physics-react")]
    public IActionResult PhysicsReact() { ViewBag.Title = "⚛️ Physics — React"; return View(); }

    [Route("/benchmark/physics-vue")]
    public IActionResult PhysicsVue() { ViewBag.Title = "🟢 Physics — Vue"; return View(); }

    [Route("/benchmark/physics-vanilla")]
    public IActionResult PhysicsVanilla() { ViewBag.Title = "🟠 Physics — Vanilla"; return View(); }

    [Route("/benchmark/3d")]
    public IActionResult WebGL() { ViewBag.Title = "💎 WebGL Forge"; return View(); }

    [Route("/benchmark/3d-react")]
    public IActionResult WebGLReact() { ViewBag.Title = "⚛️ WebGL — React"; return View(); }

    [Route("/benchmark/3d-vue")]
    public IActionResult WebGLVue() { ViewBag.Title = "🟢 WebGL — Vue"; return View(); }

    [Route("/benchmark/3d-vanilla")]
    public IActionResult WebGLVanilla() { ViewBag.Title = "🟠 WebGL — Vanilla"; return View(); }

    [Route("/benchmark/data")]
    public IActionResult DataSiege() { ViewBag.Title = "🗄️ Data Siege"; return View(); }

    [Route("/benchmark/data-react")]
    public IActionResult DataSiegeReact() { ViewBag.Title = "⚛️ Data Siege — React"; return View(); }

    [Route("/benchmark/data-vue")]
    public IActionResult DataSiegeVue() { ViewBag.Title = "🟢 Data Siege — Vue"; return View(); }

    [Route("/benchmark/data-vanilla")]
    public IActionResult DataSiegeVanilla() { ViewBag.Title = "🟠 Data Siege — Vanilla"; return View(); }

    [Route("/benchmark/crypto")]
    public IActionResult CryptoMatryoshka() { ViewBag.Title = "🔐 Crypto Matryoshka"; return View(); }

    [Route("/benchmark/crypto-react")]
    public IActionResult CryptoMatryoshkaReact() { ViewBag.Title = "⚛️ Crypto — React"; return View(); }

    [Route("/benchmark/crypto-vue")]
    public IActionResult CryptoMatryoshkaVue() { ViewBag.Title = "🟢 Crypto — Vue"; return View(); }

    [Route("/benchmark/crypto-vanilla")]
    public IActionResult CryptoMatryoshkaVanilla() { ViewBag.Title = "🟠 Crypto — Vanilla"; return View(); }

    [Route("/benchmark/tunnel")]
    public IActionResult TunnelBreach() { ViewBag.Title = "🕳️ Tunnel Breach"; return View(); }

    [Route("/benchmark/tunnel-react")]
    public IActionResult TunnelBreachReact() { ViewBag.Title = "⚛️ Tunnel — React"; return View(); }

    [Route("/benchmark/tunnel-vue")]
    public IActionResult TunnelBreachVue() { ViewBag.Title = "🟢 Tunnel — Vue"; return View(); }

    [Route("/benchmark/tunnel-vanilla")]
    public IActionResult TunnelBreachVanilla() { ViewBag.Title = "🟠 Tunnel — Vanilla"; return View(); }
}
""";

    private static string GenerateBenchmarkLayout(string name) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewBag.Title — {{name}}</title>
    <link rel="stylesheet" href="css/cepha.css" />
    <link rel="stylesheet" href="css/app.css" />
</head>
<body>
    <nav class="cepha-nav">
        <div class="cepha-nav-brand">
            <a href="/">⚡ {{name}}</a>
        </div>
        <div class="cepha-nav-links" style="flex-wrap:wrap;">
            <a href="/benchmark">Dashboard</a>
            <a href="/benchmark/stress">🔥 Stress</a>
            <a href="/benchmark/frames">🎬 Frames</a>
            <a href="/benchmark/clicks">🎯 Clicks</a>
            <a href="/benchmark/physics">🌌 Physics</a>
            <a href="/benchmark/3d">💎 3D</a>
            <a href="/benchmark/data">🗄️ Data</a>
            <a href="/benchmark/crypto">🔐 Crypto</a>
            <a href="/benchmark/tunnel">🕳️ Tunnel</a>
        </div>
    </nav>

    <main class="cepha-main">
        @RenderBody()
    </main>

    <footer class="cepha-footer">
        <p>⚡ {{name}} · Cepha Benchmark Suite · Worker Sovereignty Proof</p>
    </footer>
</body>
</html>
""";

    private static string GenerateBenchmarkStressView() => """
@{
    var workerTs = (long)ViewBag.WorkerTimestamp;
    var workerFreq = (long)ViewBag.WorkerFrequency;
}
<style>
.bench-wrap{position:relative;width:100%;max-width:1200px;margin:0 auto}
.bench-controls{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px;padding:12px;margin-bottom:10px;background:#1a1a2e;border-radius:8px;border:1px solid #333}
.bench-ctrl{display:flex;flex-direction:column;gap:3px}
.bench-ctrl label{font-size:.7rem;color:#aaa;text-transform:uppercase;letter-spacing:.05em}
.bench-ctrl input[type=range]{width:100%;accent-color:#667eea}
.bench-ctrl .val{font-size:1rem;font-weight:bold;color:#667eea;font-family:monospace}
.bench-btn{padding:7px 16px;border:none;border-radius:6px;cursor:pointer;font-weight:bold;font-size:.8rem}
.bench-btn-go{background:#28a745;color:#fff}
.bench-btn-stop{background:#dc3545;color:#fff}
.bench-btn-reset{background:#6c757d;color:#fff}
.bench-metrics{display:grid;grid-template-columns:repeat(auto-fit,minmax(110px,1fr));gap:6px;padding:8px;margin-bottom:10px;background:#0d1117;border-radius:8px;border:1px solid #30363d}
.bench-metric{text-align:center}
.bench-metric .num{font-size:1.4rem;font-weight:bold;font-family:monospace}
.bench-metric .lbl{font-size:.6rem;color:#8b949e;text-transform:uppercase}
.fps-good{color:#3fb950}.fps-warn{color:#d29922}.fps-bad{color:#f85149}
.bench-arena{position:relative;width:100%;height:480px;overflow:hidden;background:#0a0a1a;border-radius:8px;border:1px solid #333;margin-bottom:10px}
.bench-node{position:absolute;width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:.6rem;font-weight:bold;color:#fff;font-family:monospace;will-change:transform;cursor:pointer;user-select:none;backface-visibility:hidden}
.bench-node:active{filter:brightness(1.5)}
.bench-alert{position:fixed;top:12px;right:12px;padding:10px 18px;border-radius:8px;font-weight:bold;font-size:.85rem;z-index:9999;animation:alertIn .3s ease}
.bench-alert-warn{background:#d29922;color:#000}
.bench-alert-crit{background:#f85149;color:#fff}
@keyframes alertIn{from{transform:translateX(100px);opacity:0}to{transform:none;opacity:1}}
.bench-log{max-height:120px;overflow-y:auto;padding:6px;font-size:.65rem;background:#0d1117;border-radius:6px;border:1px solid #30363d;font-family:monospace;color:#8b949e}
.bench-log .err{color:#f85149}.bench-log .warn{color:#d29922}.bench-log .ok{color:#3fb950}
</style>

<div class="bench-wrap">
    <h2 style="margin:8px 0;font-size:1.2rem;">🔥 Stress Test — Click nodes to split (mitosis)</h2>

    <div class="bench-controls">
        <div class="bench-ctrl">
            <label>Nodes</label>
            <input type="range" id="ctrlCount" min="10" max="500" value="60" step="10">
            <span class="val" id="valCount">60</span>
        </div>
        <div class="bench-ctrl">
            <label>Speed</label>
            <input type="range" id="ctrlSpeed" min="1" max="120" value="30">
            <span class="val" id="valSpeed">30</span>
        </div>
        <div class="bench-ctrl">
            <label>Complexity</label>
            <input type="range" id="ctrlComplexity" min="1" max="10" value="1">
            <span class="val" id="valComplexity">1</span>
        </div>
        <div class="bench-ctrl">
            <label>Frame Target</label>
            <input type="range" id="ctrlFrameTarget" min="15" max="144" value="60">
            <span class="val" id="valFrameTarget">60</span>
        </div>
        <div style="display:flex;align-items:end;gap:6px;flex-wrap:wrap;">
            <button class="bench-btn bench-btn-go" id="btnStart">▶ START</button>
            <button class="bench-btn bench-btn-stop" id="btnStop" disabled>⏹ STOP</button>
            <button class="bench-btn bench-btn-reset" id="btnReset">↺ RESET</button>
        </div>
    </div>

    <div class="bench-metrics">
        <div class="bench-metric"><div class="num fps-good" id="metFps">0</div><div class="lbl">FPS</div></div>
        <div class="bench-metric"><div class="num" id="metNodes" style="color:#667eea">0</div><div class="lbl">Nodes</div></div>
        <div class="bench-metric"><div class="num" id="metFrames" style="color:#79c0ff">0</div><div class="lbl">Frames</div></div>
        <div class="bench-metric"><div class="num" id="metLatency" style="color:#e0e0e0">0</div><div class="lbl">Latency ms</div></div>
        <div class="bench-metric"><div class="num" id="metDropped" style="color:#f85149">0</div><div class="lbl">Dropped</div></div>
        <div class="bench-metric"><div class="num" id="metOps" style="color:#d2a8ff">0</div><div class="lbl">Ops/sec</div></div>
        <div class="bench-metric"><div class="num" id="metSplits" style="color:#f0883e">0</div><div class="lbl">Splits</div></div>
        <div class="bench-metric"><div class="num" id="metPeak" style="color:#3fb950">0</div><div class="lbl">Peak FPS</div></div>
    </div>

    <div class="bench-arena" id="arena"></div>

    <div class="bench-log" id="benchLog">
        <div class="ok">⚡ Worker ts: @workerTs (freq: @workerFreq Hz) — Click nodes to trigger mitosis</div>
    </div>
</div>

<script>
(function(){
    let running=false, nodes=[], frameCount=0, totalOps=0, droppedFrames=0, splitCount=0;
    let animId=null, lastFpsTime=performance.now(), fpsFrameCount=0, peakFps=0, currentFps=60;
    let opsThisSecond=0, lastOpsTime=performance.now();
    const WARN_FPS=35, CRIT_FPS=20;
    let lastAlertTime=0;

    const arena=document.getElementById('arena'), log=document.getElementById('benchLog');
    const ctrlCount=document.getElementById('ctrlCount'), ctrlSpeed=document.getElementById('ctrlSpeed');
    const ctrlComplexity=document.getElementById('ctrlComplexity'), ctrlFrameTarget=document.getElementById('ctrlFrameTarget');
    const valCount=document.getElementById('valCount'), valSpeed=document.getElementById('valSpeed');
    const valComplexity=document.getElementById('valComplexity'), valFrameTarget=document.getElementById('valFrameTarget');
    const btnStart=document.getElementById('btnStart'), btnStop=document.getElementById('btnStop'), btnReset=document.getElementById('btnReset');
    const metFps=document.getElementById('metFps'), metNodes=document.getElementById('metNodes');
    const metFrames=document.getElementById('metFrames'), metLatency=document.getElementById('metLatency');
    const metDropped=document.getElementById('metDropped'), metOps=document.getElementById('metOps');
    const metSplits=document.getElementById('metSplits'), metPeak=document.getElementById('metPeak');

    function addLog(msg,cls='ok'){const d=document.createElement('div');d.className=cls;d.textContent=`[${new Date().toLocaleTimeString()}] ${msg}`;log.appendChild(d);log.scrollTop=log.scrollHeight;if(log.children.length>300)log.removeChild(log.firstChild)}

    function showAlert(msg,level){
        const now=Date.now();if(now-lastAlertTime<2000)return;lastAlertTime=now;
        const a=document.createElement('div');a.className='bench-alert bench-alert-'+level;a.textContent=msg;
        document.body.appendChild(a);setTimeout(()=>a.remove(),2500);
    }

    function spawnNode(x,y,parentHue){
        const aw=arena.offsetWidth||1100,ah=arena.offsetHeight||480;
        const i=nodes.length;
        const el=document.createElement('div');el.className='bench-node';el.textContent=i;
        const hue=parentHue!==undefined?(parentHue+30+Math.random()*60)%360:Math.random()*360;
        el.style.backgroundColor=`hsl(${hue},70%,50%)`;
        if(x===undefined){x=Math.random()*(aw-40);y=Math.random()*(ah-40)}
        el.style.transform=`translate(${x}px,${y}px)`;
        el.addEventListener('click',()=>mitosis(i));
        arena.appendChild(el);
        const n={el,x,y,tx:x,ty:y,vx:(Math.random()-.5)*4,vy:(Math.random()-.5)*4,hue};
        nodes.push(n);
        return n;
    }

    function mitosis(idx){
        if(idx>=nodes.length)return;
        const p=nodes[idx];
        splitCount++;metSplits.textContent=splitCount;
        // Split into two — each inherits position + random velocity burst
        spawnNode(p.x+10,p.y-10,p.hue);
        spawnNode(p.x-10,p.y+10,p.hue);
        // Parent gets a velocity kick
        p.vx+=(Math.random()-.5)*8;p.vy+=(Math.random()-.5)*8;
        metNodes.textContent=nodes.length;
        addLog(`🧬 Mitosis at #${idx} → ${nodes.length} nodes`);
        if(nodes.length>200&&nodes.length<=210)showAlert(`⚠ ${nodes.length} nodes — approaching limit`,'warn');
        if(nodes.length>400)showAlert(`🔥 ${nodes.length} nodes — extreme load!`,'crit');
    }

    function createNodes(count){
        arena.innerHTML='';nodes=[];
        for(let i=0;i<count;i++)spawnNode();
        metNodes.textContent=nodes.length;
        addLog(`Created ${count} interactive nodes`);
    }

    function animLoop(now){
        if(!running)return;
        const speed=+ctrlSpeed.value,complexity=+ctrlComplexity.value;
        const aw=arena.offsetWidth||1100,ah=arena.offsetHeight||480;
        const t0=performance.now();

        for(let i=0;i<nodes.length;i++){
            const n=nodes[i];
            if(Math.random()<speed/600){n.tx=Math.random()*(aw-40);n.ty=Math.random()*(ah-40)}
            const dx=n.tx-n.x,dy=n.ty-n.y;
            n.vx+=dx*0.02*(speed/30);n.vy+=dy*0.02*(speed/30);
            n.vx*=0.92;n.vy*=0.92;n.x+=n.vx;n.y+=n.vy;
            if(n.x<0){n.x=0;n.vx=Math.abs(n.vx)}if(n.y<0){n.y=0;n.vy=Math.abs(n.vy)}
            if(n.x>aw-40){n.x=aw-40;n.vx=-Math.abs(n.vx)}if(n.y>ah-40){n.y=ah-40;n.vy=-Math.abs(n.vy)}

            for(let c=0;c<complexity;c++){
                const h=(Date.now()*0.1+i*17+c*31)%360;
                const sc=0.8+Math.sin(Date.now()*0.003+i)*0.3;
                const rot=Math.sin(Date.now()*0.002+i*0.5)*45;
                n.el.style.transform=`translate(${n.x}px,${n.y}px) scale(${sc}) rotate(${rot}deg)`;
                n.el.style.backgroundColor=`hsl(${h},70%,50%)`;
                n.el.style.boxShadow=`0 0 ${6+c*3}px hsl(${h},80%,60%)`;
                n.hue=h;
            }
            if(complexity<1)n.el.style.transform=`translate(${n.x}px,${n.y}px)`;
            totalOps+=complexity||1;opsThisSecond+=complexity||1;
        }

        frameCount++;fpsFrameCount++;
        const frameTime=performance.now()-t0;
        const target=+ctrlFrameTarget.value;

        if(now-lastFpsTime>=500){
            const fps=Math.round(fpsFrameCount/((now-lastFpsTime)/1000));
            currentFps=fps;
            metFps.textContent=fps;
            metFps.className='num '+(fps>=target*.9?'fps-good':fps>=target*.5?'fps-warn':'fps-bad');
            if(fps>peakFps){peakFps=fps;metPeak.textContent=peakFps}
            metLatency.textContent=frameTime.toFixed(1);
            fpsFrameCount=0;lastFpsTime=now;

            if(fps<CRIT_FPS&&nodes.length>30){showAlert(`🔴 ${fps} FPS — critical threshold!`,'crit');addLog(`🔴 CRITICAL: ${fps} FPS with ${nodes.length} nodes`,'err')}
            else if(fps<WARN_FPS&&nodes.length>30){showAlert(`⚠ ${fps} FPS — approaching limit`,'warn');addLog(`⚠ WARNING: ${fps} FPS with ${nodes.length} nodes`,'warn')}
        }
        if(now-lastOpsTime>=1000){metOps.textContent=opsThisSecond;opsThisSecond=0;lastOpsTime=now}

        metFrames.textContent=frameCount;
        if(frameTime>1000/target){droppedFrames++;metDropped.textContent=droppedFrames}

        animId=requestAnimationFrame(animLoop);
    }

    ctrlCount.oninput=()=>{valCount.textContent=ctrlCount.value};
    ctrlSpeed.oninput=()=>{valSpeed.textContent=ctrlSpeed.value};
    ctrlComplexity.oninput=()=>{valComplexity.textContent=ctrlComplexity.value};
    ctrlFrameTarget.oninput=()=>{valFrameTarget.textContent=ctrlFrameTarget.value};

    btnStart.onclick=()=>{if(running)return;running=true;btnStart.disabled=true;btnStop.disabled=false;createNodes(+ctrlCount.value);lastFpsTime=performance.now();fpsFrameCount=0;addLog(`▶ Started: ${ctrlCount.value} nodes, speed=${ctrlSpeed.value}, complexity=${ctrlComplexity.value}, target=${ctrlFrameTarget.value}fps`);animId=requestAnimationFrame(animLoop)};
    btnStop.onclick=()=>{running=false;btnStart.disabled=false;btnStop.disabled=true;if(animId)cancelAnimationFrame(animId);addLog(`⏹ Stopped — ${frameCount} frames, ${nodes.length} nodes, ${splitCount} splits, ${droppedFrames} dropped`,droppedFrames>10?'warn':'ok')};
    btnReset.onclick=()=>{running=false;btnStart.disabled=false;btnStop.disabled=true;if(animId)cancelAnimationFrame(animId);arena.innerHTML='';nodes=[];frameCount=0;totalOps=0;droppedFrames=0;splitCount=0;fpsFrameCount=0;peakFps=0;opsThisSecond=0;metFps.textContent='0';metFps.className='num fps-good';metNodes.textContent='0';metFrames.textContent='0';metLatency.textContent='0';metDropped.textContent='0';metOps.textContent='0';metSplits.textContent='0';metPeak.textContent='0';addLog('↺ Reset')};

    ctrlCount.onchange=()=>{if(running){createNodes(+ctrlCount.value);addLog(`⚡ Hot-swapped to ${ctrlCount.value} nodes`)}};

    // Auto-pilot support
    const _bench=window.__cephaBench;
    const _isAuto=_bench&&_bench.active;
    if(_isAuto){
        createNodes(200);ctrlCount.value=200;valCount.textContent='200';
        ctrlSpeed.value=150;valSpeed.textContent='150';
        running=true;btnStart.disabled=true;btnStop.disabled=false;
        lastFpsTime=performance.now();fpsFrameCount=0;
        addLog('🤖 AUTO-PILOT: 200 nodes, speed=150');
        animId=requestAnimationFrame(animLoop);
        setTimeout(()=>{
            running=false;if(animId)cancelAnimationFrame(animId);
            const score=Math.min(100,Math.round(currentFps*1.5+splitCount*2));
            _bench.scores[_bench.currentTest]=score;
            history.pushState({},'','/benchmark');
            dispatchEvent(new PopStateEvent('popstate'));
        },(_bench.duration||8)*1000);
    } else {
        createNodes(60);addLog('Ready — click START then click nodes to trigger mitosis 🧬');
    }
})();
</script>
""";

    private static string GenerateBenchmarkFramesView() => """
@{
    var workerTs = (long)ViewBag.WorkerTimestamp;
    var workerFreq = (long)ViewBag.WorkerFrequency;
}
<style>
.frame-wrap{max-width:1200px;margin:0 auto}
.frame-controls{display:flex;gap:12px;align-items:center;flex-wrap:wrap;padding:10px;margin-bottom:10px;background:#1a1a2e;border-radius:8px;border:1px solid #333}
.frame-ctrl{display:flex;flex-direction:column;gap:3px}
.frame-ctrl label{font-size:.7rem;color:#aaa;text-transform:uppercase}
.frame-ctrl input[type=range]{width:160px;accent-color:#667eea}
.frame-ctrl .val{font-size:1rem;font-weight:bold;color:#667eea;font-family:monospace}
.frame-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(80px,1fr));gap:4px;margin-bottom:10px}
.frame-cell{height:60px;border-radius:6px;display:flex;align-items:center;justify-content:center;font-size:.65rem;font-family:monospace;color:#fff;transition:none;will-change:background-color}
.frame-metrics{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:6px;padding:8px;margin-bottom:10px;background:#0d1117;border-radius:8px;border:1px solid #30363d}
.frame-metric{text-align:center}
.frame-metric .num{font-size:1.3rem;font-weight:bold;font-family:monospace}
.frame-metric .lbl{font-size:.6rem;color:#8b949e;text-transform:uppercase}
.fps-good{color:#3fb950}.fps-warn{color:#d29922}.fps-bad{color:#f85149}
.bench-btn{padding:7px 16px;border:none;border-radius:6px;cursor:pointer;font-weight:bold;font-size:.8rem}
.bench-btn-go{background:#28a745;color:#fff}
.bench-btn-stop{background:#dc3545;color:#fff}
</style>

<div class="frame-wrap">
    <h2 style="margin:8px 0;font-size:1.2rem;">🎬 Frame Pipeline — raw DOM throughput</h2>

    <div class="frame-controls">
        <div class="frame-ctrl">
            <label>Cells</label>
            <input type="range" id="fCtrlCells" min="20" max="500" value="100" step="20">
            <span class="val" id="fValCells">100</span>
        </div>
        <div class="frame-ctrl">
            <label>Updates/frame</label>
            <input type="range" id="fCtrlUpdates" min="1" max="100" value="10" step="1">
            <span class="val" id="fValUpdates">10</span>
        </div>
        <button class="bench-btn bench-btn-go" id="fBtnStart">▶ START</button>
        <button class="bench-btn bench-btn-stop" id="fBtnStop" disabled>⏹ STOP</button>
    </div>

    <div class="frame-metrics">
        <div class="frame-metric"><div class="num fps-good" id="fMetFps">0</div><div class="lbl">FPS</div></div>
        <div class="frame-metric"><div class="num" id="fMetFrames" style="color:#79c0ff">0</div><div class="lbl">Frames</div></div>
        <div class="frame-metric"><div class="num" id="fMetUpdates" style="color:#d2a8ff">0</div><div class="lbl">DOM writes</div></div>
        <div class="frame-metric"><div class="num" id="fMetLatency" style="color:#e0e0e0">0</div><div class="lbl">Frame ms</div></div>
        <div class="frame-metric"><div class="num" id="fMetPeak" style="color:#3fb950">0</div><div class="lbl">Peak FPS</div></div>
    </div>

    <div class="frame-grid" id="fGrid"></div>
    <p style="font-size:.7rem;color:#8b949e;">Worker ts: @workerTs (freq: @workerFreq Hz)</p>
</div>

<script>
(function(){
    let running=false,cells=[],frameCount=0,totalWrites=0,animId=null,lastFps=performance.now(),fpsCnt=0,peak=0,currentFps=60;
    const grid=document.getElementById('fGrid');
    const ctrlCells=document.getElementById('fCtrlCells'),ctrlUpdates=document.getElementById('fCtrlUpdates');
    const valCells=document.getElementById('fValCells'),valUpdates=document.getElementById('fValUpdates');
    const btnStart=document.getElementById('fBtnStart'),btnStop=document.getElementById('fBtnStop');
    const metFps=document.getElementById('fMetFps'),metFrames=document.getElementById('fMetFrames');
    const metUpdates=document.getElementById('fMetUpdates'),metLatency=document.getElementById('fMetLatency');
    const metPeak=document.getElementById('fMetPeak');

    function createCells(n){grid.innerHTML='';cells=[];for(let i=0;i<n;i++){const c=document.createElement('div');c.className='frame-cell';c.style.backgroundColor=`hsl(${(i*7)%360},60%,40%)`;c.textContent=i;grid.appendChild(c);cells.push(c)}}

    function loop(now){
        if(!running)return;
        const t0=performance.now();
        const upd=+ctrlUpdates.value;
        for(let u=0;u<upd;u++){
            const i=Math.floor(Math.random()*cells.length);
            const h=(Date.now()*0.2+i*13+u*37)%360;
            cells[i].style.backgroundColor=`hsl(${h},70%,50%)`;
            cells[i].textContent=Math.floor(Math.random()*999);
            totalWrites++;
        }
        frameCount++;fpsCnt++;
        const ft=performance.now()-t0;
        if(now-lastFps>=500){
            const fps=Math.round(fpsCnt/((now-lastFps)/1000));
            currentFps=fps;
            metFps.textContent=fps;metFps.className='num '+(fps>=55?'fps-good':fps>=30?'fps-warn':'fps-bad');
            if(fps>peak){peak=fps;metPeak.textContent=peak}
            metLatency.textContent=ft.toFixed(2);fpsCnt=0;lastFps=now;
        }
        metFrames.textContent=frameCount;metUpdates.textContent=totalWrites;
        animId=requestAnimationFrame(loop);
    }

    ctrlCells.oninput=()=>{valCells.textContent=ctrlCells.value;if(running)createCells(+ctrlCells.value)};
    ctrlUpdates.oninput=()=>{valUpdates.textContent=ctrlUpdates.value};
    btnStart.onclick=()=>{if(running)return;running=true;btnStart.disabled=true;btnStop.disabled=false;createCells(+ctrlCells.value);lastFps=performance.now();fpsCnt=0;animId=requestAnimationFrame(loop)};
    btnStop.onclick=()=>{running=false;btnStart.disabled=false;btnStop.disabled=true;if(animId)cancelAnimationFrame(animId)};

    // Auto-pilot support
    const _bench=window.__cephaBench;
    const _isAuto=_bench&&_bench.active;
    if(_isAuto){
        ctrlCells.value=500;valCells.textContent='500';
        ctrlUpdates.value=100;valUpdates.textContent='100';
        createCells(500);
        running=true;btnStart.disabled=true;btnStop.disabled=false;
        lastFps=performance.now();fpsCnt=0;
        animId=requestAnimationFrame(loop);
        setTimeout(()=>{
            running=false;if(animId)cancelAnimationFrame(animId);
            const score=Math.min(100,Math.round(currentFps*1.6));
            _bench.scores[_bench.currentTest]=score;
            history.pushState({},'','/benchmark');
            dispatchEvent(new PopStateEvent('popstate'));
        },(_bench.duration||8)*1000);
    } else {
        createCells(100);
    }
})();
</script>
""";
}
