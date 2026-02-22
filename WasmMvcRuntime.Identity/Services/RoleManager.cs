using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.Identity.Models;

namespace WasmMvcRuntime.Identity.Services;

/// <summary>
/// Role manager interface
/// </summary>
public interface IRoleManager
{
    Task<Role?> FindByIdAsync(int roleId);
    Task<Role?> FindByNameAsync(string roleName);
    Task<IdentityResult> CreateAsync(Role role);
    Task<IdentityResult> UpdateAsync(Role role);
    Task<IdentityResult> DeleteAsync(int roleId);
    Task<IEnumerable<Role>> GetAllRolesAsync();
    Task<IEnumerable<User>> GetUsersInRoleAsync(string roleName);
    Task<bool> RoleExistsAsync(string roleName);
}

/// <summary>
/// Role manager implementation
/// </summary>
public class RoleManager : IRoleManager
{
    private readonly DbContext _context;

    public RoleManager(DbContext context)
    {
        _context = context;
    }

    private DbSet<Role> Roles => _context.Set<Role>();

    public async Task<Role?> FindByIdAsync(int roleId)
    {
        return await Roles
            .Include(r => r.UserRoles)
                .ThenInclude(ur => ur.User)
            .FirstOrDefaultAsync(r => r.Id == roleId);
    }

    public async Task<Role?> FindByNameAsync(string roleName)
    {
        var normalized = roleName.ToUpperInvariant();
        return await Roles
            .Include(r => r.UserRoles)
                .ThenInclude(ur => ur.User)
            .FirstOrDefaultAsync(r => r.NormalizedName == normalized);
    }

    public async Task<IdentityResult> CreateAsync(Role role)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(role.Name))
            return IdentityResult.Failed("Role name is required");

        // Check if role exists
        var existing = await FindByNameAsync(role.Name);
        if (existing != null)
            return IdentityResult.Failed("Role already exists");

        // Normalize
        role.NormalizedName = role.Name.ToUpperInvariant();

        // Save
        Roles.Add(role);
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<IdentityResult> UpdateAsync(Role role)
    {
        role.NormalizedName = role.Name.ToUpperInvariant();

        Roles.Update(role);
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<IdentityResult> DeleteAsync(int roleId)
    {
        var role = await FindByIdAsync(roleId);
        if (role == null)
            return IdentityResult.Failed("Role not found");

        // Don't allow deleting default roles
        if (role.Id <= 3)
            return IdentityResult.Failed("Default roles cannot be deleted");

        Roles.Remove(role);
        await _context.SaveChangesAsync();

        return IdentityResult.Success();
    }

    public async Task<IEnumerable<Role>> GetAllRolesAsync()
    {
        return await Roles.ToListAsync();
    }

    public async Task<IEnumerable<User>> GetUsersInRoleAsync(string roleName)
    {
        var role = await FindByNameAsync(roleName);
        if (role == null)
            return Enumerable.Empty<User>();

        return role.UserRoles.Select(ur => ur.User);
    }

    public async Task<bool> RoleExistsAsync(string roleName)
    {
        var normalized = roleName.ToUpperInvariant();
        return await Roles.AnyAsync(r => r.NormalizedName == normalized);
    }
}
