using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.Identity.Models;

namespace WasmMvcRuntime.Identity.Data;

/// <summary>
/// Database context for Identity system
/// </summary>
public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Users table
    /// </summary>
    public DbSet<User> Users { get; set; } = null!;

    /// <summary>
    /// Roles table
    /// </summary>
    public DbSet<Role> Roles { get; set; } = null!;

    /// <summary>
    /// User-Role mappings
    /// </summary>
    public DbSet<UserRole> UserRoles { get; set; } = null!;

    /// <summary>
    /// User claims
    /// </summary>
    public DbSet<UserClaim> UserClaims { get; set; } = null!;

    /// <summary>
    /// Role claims
    /// </summary>
    public DbSet<RoleClaim> RoleClaims { get; set; } = null!;

    /// <summary>
    /// User tokens
    /// </summary>
    public DbSet<UserToken> UserTokens { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.NormalizedUserName);
            entity.HasIndex(e => e.NormalizedEmail);
            
            entity.Property(e => e.UserName).IsRequired();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
        });

        // Configure Role entity
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.NormalizedName);
            
            entity.Property(e => e.Name).IsRequired();
        });

        // Configure UserRole (many-to-many)
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserClaim
        modelBuilder.Entity<UserClaim>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserClaims)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure RoleClaim
        modelBuilder.Entity<RoleClaim>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RoleId);

            entity.HasOne(e => e.Role)
                .WithMany(r => r.RoleClaims)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserToken
        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Seed initial data
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Seed default roles
        var adminRole = new Role
        {
            Id = 1,
            Name = "Admin",
            NormalizedName = "ADMIN",
            Description = "System administrator - full permissions"
        };

        var userRole = new Role
        {
            Id = 2,
            Name = "User",
            NormalizedName = "USER",
            Description = "Regular user"
        };

        var moderatorRole = new Role
        {
            Id = 3,
            Name = "Moderator",
            NormalizedName = "MODERATOR",
            Description = "Moderator - limited permissions"
        };

        modelBuilder.Entity<Role>().HasData(adminRole, userRole, moderatorRole);

        // Seed default admin user
        // Password: Admin@123
        var adminUser = new User
        {
            Id = 1,
            UserName = "admin",
            NormalizedUserName = "ADMIN",
            Email = "admin@wasmapp.com",
            NormalizedEmail = "ADMIN@WASMAPP.COM",
            EmailConfirmed = true,
            PasswordHash = "AQAAAAIAAYagAAAAEGvfK8xNJ8xQqZ2Z8xL4J8xQqZ2Z8xL4J8xQqZ2Z8xL4J8xQqZ2Z8xL4J8xQqZ2Z8xL4Jw==", // Will be generated properly
            SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = "System",
            LastName = "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        modelBuilder.Entity<User>().HasData(adminUser);

        // Assign admin role to admin user
        modelBuilder.Entity<UserRole>().HasData(new UserRole
        {
            UserId = 1,
            RoleId = 1
        });
    }
}
