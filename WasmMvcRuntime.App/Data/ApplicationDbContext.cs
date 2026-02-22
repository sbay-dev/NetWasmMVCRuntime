using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.App.Models;
using WasmMvcRuntime.Identity.Models;

namespace WasmMvcRuntime.App.Data;

/// <summary>
/// Shared application database context — used by both Client (WASM) and Cepha (Server).
/// Single source of truth for the data schema.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<WeatherData> WeatherData { get; set; } = null!;
    public DbSet<City> Cities { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<UserClaim> UserClaims { get; set; } = null!;
    public DbSet<RoleClaim> RoleClaims { get; set; } = null!;
    public DbSet<UserToken> UserTokens { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WeatherData>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.City).IsRequired().HasMaxLength(100);
            e.Property(x => x.Condition).HasMaxLength(50);
            e.HasIndex(x => x.Date);
        });

        modelBuilder.Entity<City>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });

        ConfigureIdentity(modelBuilder);
        Seed(modelBuilder);
    }

    private void ConfigureIdentity(ModelBuilder m)
    {
        m.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.NormalizedUserName);
            e.HasIndex(x => x.NormalizedEmail);
            e.Property(x => x.UserName).IsRequired();
            e.Property(x => x.Email).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
        });

        m.Entity<Role>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.NormalizedName);
            e.Property(x => x.Name).IsRequired();
        });

        m.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<UserClaim>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany(u => u.UserClaims).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<RoleClaim>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RoleId);
            e.HasOne(x => x.Role).WithMany(r => r.RoleClaims).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<UserToken>(e =>
        {
            e.HasKey(x => new { x.UserId, x.LoginProvider, x.Name });
            e.HasOne(x => x.User).WithMany(u => u.UserTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void Seed(ModelBuilder m)
    {
        m.Entity<City>().HasData(
            new City { Id = 1, Name = "Riyadh", Country = "Saudi Arabia", Latitude = 24.7136, Longitude = 46.6753 },
            new City { Id = 2, Name = "Jeddah", Country = "Saudi Arabia", Latitude = 21.5433, Longitude = 39.1728 },
            new City { Id = 3, Name = "Dammam", Country = "Saudi Arabia", Latitude = 26.4367, Longitude = 50.1039 },
            new City { Id = 4, Name = "Makkah", Country = "Saudi Arabia", Latitude = 21.4225, Longitude = 39.8262 },
            new City { Id = 5, Name = "Madinah", Country = "Saudi Arabia", Latitude = 24.5247, Longitude = 39.5692 }
        );

        var rng = new Random(42);
        var conditions = new[] { "Sunny", "Partly Cloudy", "Cloudy", "Rainy", "Stormy" };
        var cities = new[] { "Riyadh", "Jeddah", "Dammam", "Makkah", "Madinah" };
        var weather = new List<WeatherData>();
        for (int i = 0; i < 50; i++)
            weather.Add(new WeatherData
            {
                Id = i + 1,
                City = cities[rng.Next(cities.Length)],
                Temperature = rng.Next(15, 45),
                Condition = conditions[rng.Next(conditions.Length)],
                Humidity = rng.Next(20, 80),
                WindSpeed = rng.Next(5, 30),
                Date = DateTime.Now.AddDays(-rng.Next(0, 30))
            });
        m.Entity<WeatherData>().HasData(weather);

        m.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Admin", NormalizedName = "ADMIN", Description = "System Administrator" },
            new Role { Id = 2, Name = "User", NormalizedName = "USER", Description = "Regular User" },
            new Role { Id = 3, Name = "Moderator", NormalizedName = "MODERATOR", Description = "Moderator" }
        );

        m.Entity<User>().HasData(new User
        {
            Id = 1, UserName = "admin", NormalizedUserName = "ADMIN",
            Email = "admin@wasmapp.com", NormalizedEmail = "ADMIN@WASMAPP.COM",
            EmailConfirmed = true, PasswordHash = "placeholder_will_be_set_on_first_run",
            SecurityStamp = "FIXED-ADMIN-SECURITY-STAMP-DO-NOT-CHANGE",
            FirstName = "Admin", LastName = "System", IsActive = true,
            LockoutEnabled = true, CreatedAt = DateTime.UtcNow
        });

        m.Entity<UserRole>().HasData(new UserRole { UserId = 1, RoleId = 1 });
    }
}
