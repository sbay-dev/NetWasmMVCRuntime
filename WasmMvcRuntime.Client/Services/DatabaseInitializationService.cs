using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.App.Data;
using WasmMvcRuntime.Identity.Services;
using WasmMvcRuntime.Identity.Models;
using WasmMvcRuntime.Data.Abstractions;

namespace WasmMvcRuntime.Client.Services;

/// <summary>
/// ✅ Database initialization with admin password fix
/// </summary>
public sealed class DatabaseInitializationService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    
    private static bool _globalInitialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public DatabaseInitializationService(
        ApplicationDbContext context, 
        IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async ValueTask InitializeAsync()
    {
        if (_globalInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_globalInitialized) return;

            await LogToConsoleAsync("🔍 Checking database...");

            // ✅ Always ensure database is created
            var created = await _context.Database.EnsureCreatedAsync();
            
            if (created)
            {
                await LogToConsoleAsync("✅ Database created successfully");
            }
            else
            {
                await LogToConsoleAsync("✅ Database already exists");
            }

            // ✅ ALWAYS check and fix admin password
            await EnsureAdminPasswordAsync();

            // ✅ Check if data exists
            await EnsureSeedDataAsync();

            _globalInitialized = true;
            await LogToConsoleAsync("✅ Database ready");
        }
        catch (Exception ex)
        {
            await LogToConsoleAsync($"❌ Database error: {ex.Message}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// ✅ CRITICAL: Ensure admin password is set correctly
    /// </summary>
    private async ValueTask EnsureAdminPasswordAsync()
    {
        try
        {
            await LogToConsoleAsync("🔐 Checking admin password...");

            // ✅ Find admin user
            var adminUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName == "admin");

            if (adminUser == null)
            {
                await LogToConsoleAsync("⚠️ Admin user not found, creating...");
                await CreateAdminUserAsync();
                return;
            }

            // ✅ Check if password is placeholder
            if (adminUser.PasswordHash == "placeholder_will_be_set_on_first_run")
            {
                await LogToConsoleAsync("🔧 Fixing admin password...");
                
                // Detach if tracked
                _context.Entry(adminUser).State = EntityState.Detached;
                
                // Reload and update
                adminUser = await _context.Users.FindAsync(adminUser.Id);
                if (adminUser != null)
                {
                    adminUser.PasswordHash = _passwordHasher.HashPassword("Admin@123");
                    adminUser.SecurityStamp = Guid.NewGuid().ToString();
                    
                    _context.Users.Update(adminUser);
                    await _context.SaveChangesAsync();
                    
                    await LogToConsoleAsync("✅ Admin password set to: Admin@123");
                }
            }
            else
            {
                await LogToConsoleAsync("✅ Admin password already set");
            }
        }
        catch (Exception ex)
        {
            await LogToConsoleAsync($"❌ Admin password error: {ex.Message}");
            // Don't throw - continue anyway
        }
    }

    /// <summary>
    /// Create admin user if doesn't exist
    /// </summary>
    private async ValueTask CreateAdminUserAsync()
    {
        try
        {
            // Check if admin role exists
            var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole == null)
            {
                adminRole = new Role
                {
                    Name = "Admin",
                    NormalizedName = "ADMIN",
                    Description = "System Administrator - Full permissions"
                };
                _context.Roles.Add(adminRole);
                await _context.SaveChangesAsync();
            }

            // Create admin user
            var admin = new User
            {
                UserName = "admin",
                NormalizedUserName = "ADMIN",
                Email = "admin@wasmapp.com",
                NormalizedEmail = "ADMIN@WASMAPP.COM",
                PasswordHash = _passwordHasher.HashPassword("Admin@123"),
                SecurityStamp = Guid.NewGuid().ToString(),
                EmailConfirmed = true,
                IsActive = true,
                LockoutEnabled = true,
                CreatedAt = DateTime.UtcNow,
                FirstName = "System",
                LastName = "Administrator"
            };

            _context.Users.Add(admin);
            await _context.SaveChangesAsync();

            // Add to admin role
            _context.UserRoles.Add(new UserRole
            {
                UserId = admin.Id,
                RoleId = adminRole.Id
            });
            await _context.SaveChangesAsync();

            await LogToConsoleAsync("✅ Admin user created successfully");
        }
        catch (Exception ex)
        {
            await LogToConsoleAsync($"❌ Create admin error: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensure seed data exists
    /// </summary>
    private async ValueTask EnsureSeedDataAsync()
    {
        try
        {
            var hasUsers = await _context.Users.AnyAsync();
            var hasWeather = await _context.WeatherData.AnyAsync();
            
            if (hasUsers && hasWeather)
            {
                await LogToConsoleAsync("✅ Seed data already exists");
                return;
            }

            await LogToConsoleAsync("🌱 Seeding initial data...");
            
            // Data will be seeded by OnModelCreating
            await _context.SaveChangesAsync();
            
            await LogToConsoleAsync("✅ Seed data complete");
        }
        catch (Exception ex)
        {
            await LogToConsoleAsync($"⚠️ Seed error: {ex.Message}");
        }
    }

    public async ValueTask ResetDatabaseAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            await LogToConsoleAsync("🗑️ Deleting database...");
            await _context.Database.EnsureDeletedAsync();
            
            await LogToConsoleAsync("📦 Creating fresh database...");
            _globalInitialized = false;
            
            await InitializeAsync();
            await LogToConsoleAsync("✅ Database reset complete");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask<DatabaseInitializationStats> GetStatsAsync()
    {
        try
        {
            if (!_globalInitialized)
            {
                await InitializeAsync();
            }

            return new DatabaseInitializationStats
            {
                WeatherDataCount = await _context.WeatherData.AsNoTracking().CountAsync(),
                CitiesCount = await _context.Cities.AsNoTracking().CountAsync(),
                UsersCount = await _context.Users.AsNoTracking().CountAsync(),
                IsInitialized = _globalInitialized
            };
        }
        catch (Exception ex)
        {
            await LogToConsoleAsync($"⚠️ Stats error: {ex.Message}");
            return new DatabaseInitializationStats();
        }
    }

    public bool IsInitialized() => _globalInitialized;

    private ValueTask LogToConsoleAsync(string message)
    {
        try
        {
            JsInterop.ConsoleLog($"[DB] {message}");
            return ValueTask.CompletedTask;
        }
        catch
        {
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record DatabaseInitializationStats
{
    public int WeatherDataCount { get; init; }
    public int CitiesCount { get; init; }
    public int UsersCount { get; init; }
    public bool IsInitialized { get; init; }
}
