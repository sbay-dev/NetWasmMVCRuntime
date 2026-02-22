using System.Runtime.Versioning;
using System.Security.Claims;
using WasmMvcRuntime.Identity.Models;
using WasmMvcRuntime.Identity.Services;

namespace WasmMvcRuntime.Client.Services;

/// <summary>
/// „œÌ— «·„’«œﬁ… ··„⁄„«—Ì… «·ÃœÌœ… WASM SDK
/// Ì” »œ· IdentityAuthenticationStateProvider (Blazor) »‰„ÿ „»«‘— »œÊ‰ Blazor
/// </summary>
[SupportedOSPlatform("browser")]
public class WasmAuthManager
{
    private readonly IUserManager _userManager;
    private readonly ISessionStorageService _sessionStorage;
    private User? _currentUser;
    private bool _isInitialized;

    public WasmAuthManager(
        IUserManager userManager,
        ISessionStorageService sessionStorage)
    {
        _userManager = userManager;
        _sessionStorage = sessionStorage;
    }

    /// <summary>
    /// «·„” Œœ„ «·Õ«·Ì
    /// </summary>
    public User? CurrentUser => _currentUser;

    /// <summary>
    /// Â· «·„” Œœ„ „”Ã· «·œŒÊ·
    /// </summary>
    public bool IsAuthenticated => _currentUser != null;

    /// <summary>
    ///  ÂÌ∆… «·Ã·”… „‰ «· Œ“Ì‰ «·„Õ·Ì
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            var session = await _sessionStorage.GetSessionAsync();

            if (session == null)
            {
                JsInterop.ConsoleLog("[Auth] No saved session found");
                _isInitialized = true;
                return;
            }

            JsInterop.ConsoleLog($"[Auth] Restoring session for user ID: {session.UserId}");

            var user = await _userManager.FindByIdAsync(session.UserId);

            if (user == null)
            {
                JsInterop.ConsoleLog("[Auth] User not found, clearing session");
                await _sessionStorage.RemoveSessionAsync();
                _isInitialized = true;
                return;
            }

            if (user.SecurityStamp != session.SecurityStamp)
            {
                JsInterop.ConsoleLog("[Auth] Security stamp mismatch, clearing session");
                await _sessionStorage.RemoveSessionAsync();
                _isInitialized = true;
                return;
            }

            if (!user.IsActive)
            {
                JsInterop.ConsoleLog("[Auth] User is inactive, clearing session");
                await _sessionStorage.RemoveSessionAsync();
                _isInitialized = true;
                return;
            }

            _currentUser = user;
            JsInterop.ConsoleLog($"[Auth] Session restored for user: {user.UserName}");
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleError($"[Auth] Error initializing session: {ex.Message}");
            await _sessionStorage.RemoveSessionAsync();
        }

        _isInitialized = true;
    }

    /// <summary>
    ///  ”ÃÌ· œŒÊ· «·„” Œœ„
    /// </summary>
    public async Task MarkUserAsAuthenticatedAsync(User user)
    {
        _currentUser = await _userManager.FindByIdAsync(user.Id) ?? user;
        JsInterop.ConsoleLog($"[Auth] User authenticated: {_currentUser.UserName}");
    }

    /// <summary>
    ///  ”ÃÌ· Œ—ÊÃ «·„” Œœ„
    /// </summary>
    public void MarkUserAsLoggedOut()
    {
        var username = _currentUser?.UserName;
        _currentUser = null;
        JsInterop.ConsoleLog($"[Auth] User logged out: {username}");
    }

    /// <summary>
    /// «·Õ’Ê· ⁄·Ï ClaimsPrincipal
    /// </summary>
    public ClaimsPrincipal GetClaimsPrincipal()
    {
        if (_currentUser == null)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _currentUser.Id.ToString()),
            new(ClaimTypes.Name, _currentUser.UserName),
            new(ClaimTypes.Email, _currentUser.Email),
            new("FullName", _currentUser.FullName)
        };

        if (!string.IsNullOrEmpty(_currentUser.FirstName))
            claims.Add(new Claim(ClaimTypes.GivenName, _currentUser.FirstName));

        if (!string.IsNullOrEmpty(_currentUser.LastName))
            claims.Add(new Claim(ClaimTypes.Surname, _currentUser.LastName));

        foreach (var userRole in _currentUser.UserRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, userRole.Role.Name));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Identity"));
    }

    /// <summary>
    /// «· Õﬁﬁ „‰ œÊ— «·„” Œœ„
    /// </summary>
    public async Task<bool> IsInRoleAsync(string roleName)
    {
        if (_currentUser == null)
            return false;

        return await _userManager.IsInRoleAsync(_currentUser, roleName);
    }

    /// <summary>
    /// «·Õ’Ê· ⁄·Ï „⁄—› «·„” Œœ„ «·Õ«·Ì
    /// </summary>
    public int? GetCurrentUserId() => _currentUser?.Id;

    /// <summary>
    /// «·Õ’Ê· ⁄·Ï «”„ «·„” Œœ„ «·Õ«·Ì
    /// </summary>
    public string? GetCurrentUserName() => _currentUser?.UserName;
}
