using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Client;
using WasmMvcRuntime.App.Data;
using WasmMvcRuntime.App.Repositories;
using WasmMvcRuntime.Client.Services;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.Core;
using WasmMvcRuntime.Identity.Services;

// ─── New WASM SDK Architecture ────────────────────────────────
JsInterop.ConsoleLog("🚀 WasmMvcRuntime starting...");

// ✅ Manual service container setup (without Blazor WebAssemblyHostBuilder)
var services = new ServiceCollection();

// ✅ HttpClient
services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri("./"),
    Timeout = TimeSpan.FromSeconds(30)
});

// ✅ DbContext - SQLite database
services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite("Data Source=wasmapp.db");
    options.EnableSensitiveDataLogging(false);
    options.EnableDetailedErrors(false);
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
}, ServiceLifetime.Scoped);

services.AddScoped<DbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

// ✅ Repositories
services.AddScoped<IWeatherRepository, WeatherRepository>();
services.AddScoped<ICityRepository, CityRepository>();

// ✅ Database services
services.AddScoped<DatabaseInitializationService>();
services.AddScoped<DatabaseManagementService>();

// ✅ Session Storage - uses [JSImport] instead of IJSRuntime
services.AddScoped<ISessionStorageService, WasmSessionStorageService>();

// ✅ Identity services
services.AddSingleton<IPasswordHasher, PasswordHasher>();
services.AddScoped<IUserManager, UserManager>();
services.AddScoped<IRoleManager, RoleManager>();
services.AddScoped<ISignInManager, SignInManager>();

// ✅ Authentication Manager - without Blazor AuthenticationStateProvider
services.AddScoped<WasmAuthManager>();

// ✅ Account API Service
services.AddScoped<AccountApiService>();

// ✅ MVC Engine
services.AddSingleton<IMvcEngine, MvcEngine>();

// ✅ SignalR Engine
services.AddSingleton<ISignalREngine>(sp => 
{
    var engine = new SignalREngine(sp);
    engine.OnClientEvent = (hubName, method, connectionId, argsJson) =>
    {
        JsInterop.DispatchHubEvent(hubName, method, connectionId, argsJson);
    };
    return engine;
});

var serviceProvider = services.BuildServiceProvider();

// ✅ Initialize the database
JsInterop.ConsoleLog("📦 Initializing database...");
using (var scope = serviceProvider.CreateScope())
{
    var dbInit = scope.ServiceProvider.GetRequiredService<DatabaseInitializationService>();
    await dbInit.InitializeAsync();
}
JsInterop.ConsoleLog("✅ Database ready");

// ✅ Initialize the view system - load embedded .cshtml templates
WasmMvcRuntime.Abstractions.ViewResult.Configure();
JsInterop.ConsoleLog("✅ View system configured");

// ✅ Register the navigation handler
var mvcEngine = serviceProvider.GetRequiredService<IMvcEngine>();
JsExports.RegisterNavigateHandler(async (path) =>
{
    JsInterop.ConsoleLog($"[Router] Navigating to: {path}");
    try
    {
        using var scope = serviceProvider.CreateScope();
        var context = new WasmMvcRuntime.Abstractions.InternalHttpContext
        {
            Path = path,
            Method = "GET",
            RequestServices = scope.ServiceProvider
        };

        await mvcEngine.ProcessRequestAsync(context);

        // Render the MVC result on the page
        if (!string.IsNullOrEmpty(context.ResponseBody))
        {
            if (context.ContentType == "application/json")
            {
                // API response: show formatted JSON
                var escaped = context.ResponseBody.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                JsInterop.SetInnerHTML("#app", $"<pre style='background:#1e1e1e;color:#d4d4d4;padding:20px;border-radius:8px;overflow:auto;'>{escaped}</pre>");
            }
            else
            {
                JsInterop.SetInnerHTML("#app", context.ResponseBody);
            }
        }
    }
    catch (Exception ex)
    {
        JsInterop.ConsoleError($"[Router] Error: {ex.Message}");
        JsInterop.SetInnerHTML("#app", $"<div class='error'><h2>Error</h2><p>{ex.Message}</p></div>");
    }
});

// ✅ FetchRoute handler - processes the route and returns the result without modifying the DOM
JsExports.RegisterFetchRouteHandler(async (path) =>
{
    try
    {
        using var scope = serviceProvider.CreateScope();
        var context = new WasmMvcRuntime.Abstractions.InternalHttpContext
        {
            Path = path,
            Method = "GET",
            RequestServices = scope.ServiceProvider
        };

        await mvcEngine.ProcessRequestAsync(context);
        return context.ResponseBody ?? "";
    }
    catch (Exception ex)
    {
        return $"{{\"error\": \"{ex.Message}\"}}";
    }
});

// ✅ SignalR Hub handlers
var signalREngine = serviceProvider.GetRequiredService<ISignalREngine>();
JsInterop.ConsoleLog($"✅ SignalR hubs discovered: {string.Join(", ", signalREngine.GetHubNames())}");

JsExports.RegisterHubConnectHandler(async (hubName) =>
{
    return await signalREngine.ConnectAsync(hubName);
});

JsExports.RegisterHubDisconnectHandler(async (hubName, connectionId) =>
{
    await signalREngine.DisconnectAsync(hubName, connectionId);
});

JsExports.RegisterHubInvokeHandler(async (hubName, method, connectionId, argsJson) =>
{
    return await signalREngine.InvokeAsync(hubName, method, connectionId, argsJson);
});

// ✅ Navigate to the current path (or home page if the path is empty)
var currentPath = JsInterop.GetCurrentPath();
if (string.IsNullOrEmpty(currentPath))
    currentPath = "/";
await JsExports.Navigate(currentPath);

JsInterop.ConsoleLog("✅ WasmMvcRuntime is running!");

// ✅ Keep the runtime running
await Task.Delay(Timeout.Infinite);
