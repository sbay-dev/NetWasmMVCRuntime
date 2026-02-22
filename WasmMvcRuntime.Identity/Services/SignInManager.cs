using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.Identity.Models;

namespace WasmMvcRuntime.Identity.Services;

/// <summary>
/// Sign-in manager interface
/// </summary>
public interface ISignInManager
{
    Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent = false);
    Task<SignInResult> CheckPasswordSignInAsync(User user, string password, bool isPersistent = false);
    Task SignOutAsync();
    Task<bool> IsSignedInAsync(User? user);
    Task<bool> CanSignInAsync(User user);
}

/// <summary>
/// Sign-in manager implementation
/// </summary>
public class SignInManager : ISignInManager
{
    private readonly IUserManager _userManager;
    private readonly DbContext _context;
    private readonly ISessionStorageService _sessionStorage;
    private readonly IdentityTriggers _triggers;
    private User? _currentUser;

    public SignInManager(
        IUserManager userManager, 
        DbContext context,
        ISessionStorageService sessionStorage,
        IdentityTriggers triggers)
    {
        _userManager = userManager;
        _context = context;
        _sessionStorage = sessionStorage;
        _triggers = triggers;
    }

    public async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent = false)
    {
        // ✅ Load user with roles
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return SignInResult.Failed("Invalid username or password");
        }

        return await CheckPasswordSignInAsync(user, password, isPersistent);
    }

    public async Task<SignInResult> CheckPasswordSignInAsync(User user, string password, bool isPersistent = false)
    {
        // Check if user can sign in
        if (!await CanSignInAsync(user))
        {
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
            {
                return SignInResult.LockedOut($"Account is locked until {user.LockoutEnd.Value.LocalDateTime:HH:mm}");
            }

            if (!user.IsActive)
            {
                return SignInResult.Failed("Account is inactive");
            }

            return SignInResult.Failed("Unable to sign in");
        }

        // Check password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, password);
        if (!isPasswordValid)
        {
            // Increment failed access count
            await _userManager.AccessFailedAsync(user);
            
            // ✅ Detach and reload
            _context.Entry(user).State = EntityState.Detached;
            user = await _userManager.FindByIdAsync(user.Id) ?? user;
            
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
            {
                _triggers.FireLockedOut(user.UserName, user.LockoutEnd.Value);
                return SignInResult.LockedOut($"Account has been locked after multiple failed attempts until {user.LockoutEnd.Value.LocalDateTime:HH:mm}");
            }

            _triggers.FireSignInFailed(user.UserName, "Incorrect password");
            return SignInResult.Failed($"Incorrect password. Remaining attempts: {5 - user.AccessFailedCount}");
        }

        // ✅ Reset failed access count
        await _userManager.ResetAccessFailedCountAsync(user);

        // ✅ Detach and reload to get fresh data with roles
        _context.Entry(user).State = EntityState.Detached;
        user = await _userManager.FindByIdAsync(user.Id) ?? user;

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // ✅ Reload again to get updated user with roles
        _context.Entry(user).State = EntityState.Detached;
        user = await _userManager.FindByIdAsync(user.Id) ?? user;

        // Set current user
        _currentUser = user;

        // ✅ Save session to storage
        await SaveSessionAsync(user, isPersistent);

        // 🧬 Fire atomic probe — centralized trigger
        var roles = await _userManager.GetRolesAsync(user);
        await _triggers.FireSignedInAsync(user.Id.ToString(), user.UserName, roles.ToArray());

        return SignInResult.Success(user);
    }

    public async Task SignOutAsync()
    {
        // Capture identity before clearing (it vanishes after logout)
        var userId = _currentUser?.Id.ToString() ?? "";
        var userName = _currentUser?.UserName ?? "";
        
        _currentUser = null;
        
        // ✅ Remove session from storage
        await _sessionStorage.RemoveSessionAsync();

        // 🧬 Fire atomic probe — centralized trigger
        if (!string.IsNullOrEmpty(userId))
            await _triggers.FireSignedOutAsync(userId, userName);
    }

    public Task<bool> IsSignedInAsync(User? user)
    {
        return Task.FromResult(user != null && _currentUser != null && user.Id == _currentUser.Id);
    }

    public Task<bool> CanSignInAsync(User user)
    {
        // Check if account is locked
        if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return Task.FromResult(false);
        }

        // Check if account is active
        if (!user.IsActive)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Save user session to storage
    /// </summary>
    private async Task SaveSessionAsync(User user, bool rememberMe)
    {
        var roles = await _userManager.GetRolesAsync(user);
        
        var expirationDays = rememberMe ? 30 : 1; // 30 days if remember me, otherwise 1 day
        
        var session = new SessionData
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            FullName = user.FullName,
            Roles = roles.ToList(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(expirationDays),
            RememberMe = rememberMe,
            SecurityStamp = user.SecurityStamp,
            Claims = new Dictionary<string, string>
            {
                ["FirstName"] = user.FirstName ?? "",
                ["LastName"] = user.LastName ?? "",
                ["PhoneNumber"] = user.PhoneNumber ?? ""
            }
        };

        await _sessionStorage.SaveSessionAsync(session);
    }
}

/// <summary>
/// Sign-in result
/// </summary>
public class SignInResult
{
    public bool Succeeded { get; set; }
    public bool IsLockedOut { get; set; }
    public string? ErrorMessage { get; set; }
    public User? User { get; set; }

    public static SignInResult Success(User user) => new SignInResult 
    { 
        Succeeded = true, 
        User = user 
    };

    public static SignInResult Failed(string errorMessage) => new SignInResult 
    { 
        Succeeded = false, 
        ErrorMessage = errorMessage 
    };

    public static SignInResult LockedOut(string errorMessage) => new SignInResult 
    { 
        Succeeded = false, 
        IsLockedOut = true, 
        ErrorMessage = errorMessage 
    };
}
