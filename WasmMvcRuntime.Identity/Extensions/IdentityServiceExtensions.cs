using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Identity.Data;
using WasmMvcRuntime.Identity.Services;

namespace WasmMvcRuntime.Identity.Extensions;

/// <summary>
/// Extension methods for registering Identity services
/// </summary>
public static class IdentityServiceExtensions
{
    /// <summary>
    /// Adds Identity services to the service collection
    /// </summary>
    public static IServiceCollection AddWasmIdentity(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? optionsAction = null)
    {
        // Register DbContext
        if (optionsAction != null)
        {
            services.AddDbContext<IdentityDbContext>(optionsAction);
        }
        else
        {
            // Default SQLite configuration
            services.AddDbContext<IdentityDbContext>(options =>
                options.UseSqlite("Data Source=identity.db"));
        }

        // Register DbContext as base type for DI resolution
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<IdentityDbContext>());

        // Register Identity services
        services.AddSingleton<IdentityTriggers>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ISessionStorageService, SessionStorageService>();
        services.AddScoped<IUserManager, UserManager>();
        services.AddScoped<IRoleManager, RoleManager>();
        services.AddScoped<ISignInManager, SignInManager>();

        return services;
    }

    /// <summary>
    /// Initializes Identity database (creates tables and seed data)
    /// </summary>
    public static async Task InitializeIdentityDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();

        // Apply any pending migrations
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Any())
        {
            await context.Database.MigrateAsync();
        }
    }
}
