using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.App.Data;
using WasmMvcRuntime.App.Repositories;
using WasmMvcRuntime.Cepha.SSE;
using WasmMvcRuntime.Core;
using WasmMvcRuntime.Identity.Data;
using WasmMvcRuntime.Identity.Services;

namespace WasmMvcRuntime.Cepha.Kit;

/// <summary>
/// Cepha Kit — one-line server setup for WasmMvcRuntime.
/// <code>services.AddCephaKit();</code>
/// Registers all services needed to run Cepha as a backend for WasmMvcRuntime.Client:
/// MVC engine, SignalR engine, SSE, Identity, EF Core + SQLite, Repositories.
/// </summary>
public static class CephaKit
{
    /// <summary>
    /// Registers all Cepha server services with sensible defaults.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration overrides</param>
    public static IServiceCollection AddCephaKit(
        this IServiceCollection services,
        Action<CephaKitOptions>? configure = null)
    {
        var options = new CephaKitOptions();
        configure?.Invoke(options);

        // ─── Application Database ────────────────────────────
        services.AddDbContext<ApplicationDbContext>(db =>
        {
            db.UseSqlite(options.ConnectionString);
            db.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        }, ServiceLifetime.Scoped);

        // ─── Identity Database ───────────────────────────────
        services.AddDbContext<IdentityDbContext>(db =>
        {
            db.UseSqlite(options.IdentityConnectionString);
            db.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        }, ServiceLifetime.Scoped);

        // ─── Repositories ────────────────────────────────────
        services.AddScoped<IWeatherRepository, WeatherRepository>();
        services.AddScoped<ICityRepository, CityRepository>();

        // ─── Identity Services ───────────────────────────────
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserManager, UserManager>();
        services.AddScoped<IRoleManager, RoleManager>();
        services.AddScoped<ISignInManager, SignInManager>();
        services.AddScoped<ISessionStorageService, Services.CephaSessionStorageService>();

        // ─── HttpClient ──────────────────────────────────────
        services.AddScoped(sp => new HttpClient
        {
            BaseAddress = new Uri(options.BaseAddress),
            Timeout = TimeSpan.FromSeconds(30)
        });

        // ─── MVC Engine ──────────────────────────────────────
        services.AddSingleton<IMvcEngine, MvcEngine>();

        // ─── SignalR Engine ──────────────────────────────────
        services.AddSingleton<ISignalREngine>(sp =>
        {
            var engine = new SignalREngine(sp);
            engine.OnClientEvent = (hubName, method, connectionId, argsJson) =>
            {
                CephaInterop.DispatchHubEvent(hubName, method, connectionId, argsJson);
            };
            return engine;
        });

        // ─── SSE Infrastructure ──────────────────────────────
        services.AddSingleton<SseConnectionManager>();
        services.AddSingleton<SseMiddleware>();

        // ─── Cepha Server ────────────────────────────────────
        services.AddSingleton<CephaServer>();

        return services;
    }

    /// <summary>
    /// Initializes Cepha databases and starts the server.
    /// Call after BuildServiceProvider().
    /// </summary>
    public static async Task<IServiceProvider> UseCephaKit(this IServiceProvider provider)
    {
        CephaInterop.ConsoleLog("🧬 Cepha Kit: initializing databases...");

        // Initialize Application DB
        using (var scope = provider.CreateScope())
        {
            var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await appDb.Database.EnsureCreatedAsync();
        }

        // Initialize Identity DB
        using (var scope = provider.CreateScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await identityDb.Database.EnsureCreatedAsync();
        }

        // Configure views
        ViewResult.Configure();

        // Start the server
        var server = provider.GetRequiredService<CephaServer>();
        server.Start();

        CephaInterop.ConsoleLog("✅ Cepha Kit: all systems operational");
        return provider;
    }
}

/// <summary>
/// Configuration options for CephaKit.
/// </summary>
public class CephaKitOptions
{
    /// <summary>Application database connection string. Default: "Data Source=cepha.db"</summary>
    public string ConnectionString { get; set; } = "Data Source=cepha.db";

    /// <summary>Identity database connection string. Default: "Data Source=cepha_identity.db"</summary>
    public string IdentityConnectionString { get; set; } = "Data Source=cepha_identity.db";

    /// <summary>Base address for HttpClient. Default: "http://localhost/"</summary>
    public string BaseAddress { get; set; } = "http://localhost/";
}
