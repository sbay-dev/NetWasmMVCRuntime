using WasmMvcRuntime.Identity.Services;
using WasmMvcRuntime.Identity.ViewModels;
using WasmMvcRuntime.Identity.Models;
using WasmMvcRuntime.Client.Services;

namespace WasmMvcRuntime.Client.Services;

/// <summary>
/// Account service for authentication operations.
/// Provides authentication operations for the new WASM SDK architecture.
/// </summary>
public class AccountApiService
{
    private readonly IUserManager _userManager;
    private readonly ISignInManager _signInManager;
    private readonly WasmAuthManager _authManager;

    public AccountApiService(
        IUserManager userManager,
        ISignInManager signInManager,
        WasmAuthManager authManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _authManager = authManager;
    }

    /// <summary>
    /// Login method
    /// </summary>
    public async Task<LoginResponse> Login(LoginViewModel model)
    {
        try
        {
            var result = await _signInManager.PasswordSignInAsync(
                model.UserName,
                model.Password,
                model.RememberMe
            );

            if (result.Succeeded && result.User != null)
            {
                // Update authentication state
                await _authManager.MarkUserAsAuthenticatedAsync(result.User);

                return new LoginResponse
                {
                    Success = true,
                    Message = $"Welcome {result.User.FullName}!",
                    User = new UserInfo
                    {
                        Id = result.User.Id,
                        UserName = result.User.UserName,
                        Email = result.User.Email,
                        FullName = result.User.FullName
                    }
                };
            }
            else if (result.IsLockedOut)
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Your account is temporarily locked"
                };
            }
            else
            {
                return new LoginResponse
                {
                    Success = false,
                    Message = result.ErrorMessage ?? "Invalid username or password"
                };
            }
        }
        catch (Exception ex)
        {
            return new LoginResponse
            {
                Success = false,
                Message = $"An error occurred: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Register method
    /// </summary>
    public async Task<RegisterResponse> Register(RegisterViewModel model)
    {
        try
        {
            var user = new User
            {
                UserName = model.UserName,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                PhoneNumber = model.PhoneNumber,
                EmailConfirmed = false,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                // Add default User role
                await _userManager.AddToRoleAsync(user, "User");

                // Auto sign-in
                var signInResult = await _signInManager.PasswordSignInAsync(
                    model.UserName,
                    model.Password,
                    false
                );

                if (signInResult.Succeeded && signInResult.User != null)
                {
                    await _authManager.MarkUserAsAuthenticatedAsync(signInResult.User);
                    
                    return new RegisterResponse
                    {
                        Success = true,
                        Message = "Account created successfully",
                        User = new UserInfo
                        {
                            Id = signInResult.User.Id,
                            UserName = signInResult.User.UserName,
                            Email = signInResult.User.Email,
                            FullName = signInResult.User.FullName
                        }
                    };
                }

                return new RegisterResponse
                {
                    Success = true,
                    Message = "Account created successfully. You can now sign in."
                };
            }
            else
            {
                return new RegisterResponse
                {
                    Success = false,
                    Errors = result.Errors.ToList()
                };
            }
        }
        catch (Exception ex)
        {
            return new RegisterResponse
            {
                Success = false,
                Errors = new List<string> { $"An error occurred: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Logout method
    /// </summary>
    public async Task<LogoutResponse> Logout()
    {
        await _signInManager.SignOutAsync();
        _authManager.MarkUserAsLoggedOut();
        
        return new LogoutResponse
        {
            Success = true,
            Message = "Signed out successfully"
        };
    }

    /// <summary>
    /// Get current user profile
    /// </summary>
    public async Task<ProfileResponse> GetProfile()
    {
        var userId = _authManager.GetCurrentUserId();
        
        if (userId == null)
        {
            return new ProfileResponse
            {
                Success = false,
                Message = "You must sign in first"
            };
        }

        var user = await _userManager.FindByIdAsync(userId.Value);
        
        if (user == null)
        {
            return new ProfileResponse
            {
                Success = false,
                Message = "User not found"
            };
        }

        var roles = await _userManager.GetRolesAsync(user);

        return new ProfileResponse
        {
            Success = true,
            Profile = new UserProfileViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Roles = roles.ToList()
            }
        };
    }

    /// <summary>
    /// Change password method
    /// </summary>
    public async Task<ChangePasswordResponse> ChangePassword(ChangePasswordViewModel model)
    {
        var userId = _authManager.GetCurrentUserId();
        
        if (userId == null)
        {
            return new ChangePasswordResponse
            {
                Success = false,
                Message = "You must sign in first"
            };
        }

        var user = await _userManager.FindByIdAsync(userId.Value);
        
        if (user == null)
        {
            return new ChangePasswordResponse
            {
                Success = false,
                Message = "User not found"
            };
        }

        var result = await _userManager.ChangePasswordAsync(
            user,
            model.CurrentPassword,
            model.NewPassword
        );

        if (result.Succeeded)
        {
            return new ChangePasswordResponse
            {
                Success = true,
                Message = "Password changed successfully"
            };
        }
        else
        {
            return new ChangePasswordResponse
            {
                Success = false,
                Errors = result.Errors.ToList()
            };
        }
    }
}

// Response DTOs
public class LoginResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public UserInfo? User { get; set; }
}

public class RegisterResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public UserInfo? User { get; set; }
    public List<string>? Errors { get; set; }
}

public class LogoutResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class ProfileResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public UserProfileViewModel? Profile { get; set; }
}

public class ChangePasswordResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
}

public class UserInfo
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}
