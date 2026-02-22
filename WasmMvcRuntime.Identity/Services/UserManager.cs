using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.Identity.Models;

namespace WasmMvcRuntime.Identity.Services;

/// <summary>
/// User manager service interface
/// </summary>
public interface IUserManager
{
    Task<User?> FindByIdAsync(int userId);
    Task<User?> FindByNameAsync(string userName);
    Task<User?> FindByEmailAsync(string email);
    Task<IdentityResult> CreateAsync(User user, string password);
    Task<IdentityResult> UpdateAsync(User user);
    Task<IdentityResult> DeleteAsync(int userId);
    Task<bool> CheckPasswordAsync(User user, string password);
    Task<IdentityResult> ChangePasswordAsync(User user, string currentPassword, string newPassword);
    Task<IdentityResult> ResetPasswordAsync(User user, string newPassword);
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<IEnumerable<string>> GetRolesAsync(User user);
    Task<IdentityResult> AddToRoleAsync(User user, string roleName);
    Task<IdentityResult> RemoveFromRoleAsync(User user, string roleName);
    Task<bool> IsInRoleAsync(User user, string roleName);
    Task<IdentityResult> SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd);
    Task<IdentityResult> AccessFailedAsync(User user);
    Task<IdentityResult> ResetAccessFailedCountAsync(User user);
    Task<IEnumerable<User>> GetUsersInRoleAsync(string roleName);
}

/// <summary>
/// User manager implementation
/// </summary>
public class UserManager : IUserManager
{
    private readonly DbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public UserManager(DbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    private DbSet<User> Users => _context.Set<User>();
    private DbSet<Role> Roles => _context.Set<Role>();
    private DbSet<UserRole> UserRoles => _context.Set<UserRole>();

    public async Task<User?> FindByIdAsync(int userId)
    {
        return await Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<User?> FindByNameAsync(string userName)
    {
        var normalized = userName.ToUpperInvariant();
        return await Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalized);
    }

    public async Task<User?> FindByEmailAsync(string email)
    {
        var normalized = email.ToUpperInvariant();
        return await Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
    }

    public async Task<IdentityResult> CreateAsync(User user, string password)
    {
        // Validate
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(user.UserName))
            errors.Add("Username is required");

        if (string.IsNullOrWhiteSpace(user.Email))
            errors.Add("Email is required");

        if (string.IsNullOrWhiteSpace(password))
            errors.Add("Password is required");

        if (password.Length < 6)
            errors.Add("Password must be at least 6 characters");

        // ✅ Check if username exists (AsNoTracking to avoid conflicts)
        var existingUser = await Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedUserName == user.UserName.ToUpperInvariant());
        if (existingUser != null)
            errors.Add("Username already exists");

        // ✅ Check if email exists (AsNoTracking)
        var existingEmail = await Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == user.Email.ToUpperInvariant());
        if (existingEmail != null)
            errors.Add("Email is already in use");

        if (errors.Any())
            return IdentityResult.Failed(errors.ToArray());

        // Normalize
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.NormalizedEmail = user.Email.ToUpperInvariant();

        // Hash password
        user.PasswordHash = _passwordHasher.HashPassword(password);
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.CreatedAt = DateTime.UtcNow;
        user.IsActive = true;
        user.LockoutEnabled = true;

        // Save
        Users.Add(user);
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<IdentityResult> UpdateAsync(User user)
    {
        // ✅ Detach any existing tracked entity with same ID
        var tracked = _context.ChangeTracker.Entries<User>()
            .FirstOrDefault(e => e.Entity.Id == user.Id);
        
        if (tracked != null && tracked.Entity != user)
        {
            _context.Entry(tracked.Entity).State = EntityState.Detached;
        }

        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.UpdatedAt = DateTime.UtcNow;

        _context.Entry(user).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<IdentityResult> DeleteAsync(int userId)
    {
        var user = await Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
            
        if (user == null)
            return IdentityResult.Failed("User not found");

        Users.Remove(user);
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<bool> CheckPasswordAsync(User user, string password)
    {
        if (user == null || string.IsNullOrWhiteSpace(password))
            return false;

        return await Task.Run(() => _passwordHasher.VerifyPassword(user.PasswordHash, password));
    }

    public async Task<IdentityResult> ChangePasswordAsync(User user, string currentPassword, string newPassword)
    {
        if (!await CheckPasswordAsync(user, currentPassword))
            return IdentityResult.Failed("Current password is incorrect");

        if (newPassword.Length < 6)
            return IdentityResult.Failed("New password must be at least 6 characters");

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        
        // ✅ Keep admin security stamp fixed for session persistence
        if (user.UserName?.ToLowerInvariant() != "admin")
        {
            user.SecurityStamp = Guid.NewGuid().ToString();
        }
        
        user.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(user);

        return IdentityResult.Success();
    }

    public async Task<IdentityResult> ResetPasswordAsync(User user, string newPassword)
    {
        if (newPassword.Length < 6)
            return IdentityResult.Failed("Password must be at least 6 characters");

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        
        // ✅ Keep admin security stamp fixed for session persistence
        if (user.UserName?.ToLowerInvariant() != "admin")
        {
            user.SecurityStamp = Guid.NewGuid().ToString();
        }
        
        user.UpdatedAt = DateTime.UtcNow;

        await UpdateAsync(user);

        return IdentityResult.Success();
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> GetRolesAsync(User user)
    {
        var userWithRoles = await Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        return userWithRoles?.UserRoles.Select(ur => ur.Role.Name) ?? Enumerable.Empty<string>();
    }

    public async Task<IdentityResult> AddToRoleAsync(User user, string roleName)
    {
        var role = await Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant());
            
        if (role == null)
            return IdentityResult.Failed("Role not found");

        var existingUserRole = await UserRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id);

        if (existingUserRole != null)
            return IdentityResult.Failed("User already has this role");

        UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id
        });

        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<IdentityResult> RemoveFromRoleAsync(User user, string roleName)
    {
        var role = await Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant());
            
        if (role == null)
            return IdentityResult.Failed("Role not found");

        var userRole = await UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id);

        if (userRole == null)
            return IdentityResult.Failed("User does not have this role");

        UserRoles.Remove(userRole);
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<bool> IsInRoleAsync(User user, string roleName)
    {
        var roles = await GetRolesAsync(user);
        return roles.Any(r => r.Equals(roleName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IdentityResult> SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd)
    {
        user.LockoutEnd = lockoutEnd;
        await UpdateAsync(user);
        return IdentityResult.Success();
    }

    public async Task<IdentityResult> AccessFailedAsync(User user)
    {
        user.AccessFailedCount++;

        // Lock account after 5 failed attempts
        if (user.AccessFailedCount >= 5)
        {
            user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        }

        await UpdateAsync(user);
        return IdentityResult.Success();
    }

    public async Task<IdentityResult> ResetAccessFailedCountAsync(User user)
    {
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await UpdateAsync(user);
        return IdentityResult.Success();
    }

    public async Task<IEnumerable<User>> GetUsersInRoleAsync(string roleName)
    {
        var role = await Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.NormalizedName == roleName.ToUpperInvariant());
            
        if (role == null)
            return Enumerable.Empty<User>();

        return await Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.UserRoles.Any(ur => ur.RoleId == role.Id))
            .ToListAsync();
    }
}

/// <summary>
/// Identity operation result
/// </summary>
public class IdentityResult
{
    public bool Succeeded { get; set; }
    public string[] Errors { get; set; } = Array.Empty<string>();

    public static IdentityResult Success() => new IdentityResult { Succeeded = true };
    public static IdentityResult Failed(params string[] errors) => new IdentityResult { Succeeded = false, Errors = errors };
}
