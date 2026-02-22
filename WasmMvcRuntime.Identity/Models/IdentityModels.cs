using System.ComponentModel.DataAnnotations;

namespace WasmMvcRuntime.Identity.Models;

/// <summary>
/// User entity for identity system
/// </summary>
public class User
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Username (unique)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Email address (unique)
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Normalized username for searches
    /// </summary>
    [MaxLength(50)]
    public string NormalizedUserName { get; set; } = string.Empty;

    /// <summary>
    /// Normalized email for searches
    /// </summary>
    [MaxLength(100)]
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>
    /// Password hash
    /// </summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Security stamp (changes on password reset)
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Email confirmed
    /// </summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>
    /// Phone number
    /// </summary>
    [Phone]
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Phone number confirmed
    /// </summary>
    public bool PhoneNumberConfirmed { get; set; }

    /// <summary>
    /// Two-factor authentication enabled
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>
    /// Lockout end date
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>
    /// Lockout enabled
    /// </summary>
    public bool LockoutEnabled { get; set; } = true;

    /// <summary>
    /// Failed access attempts
    /// </summary>
    public int AccessFailedCount { get; set; }

    /// <summary>
    /// User creation date
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last updated date
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Last login date
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// User is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// First name
    /// </summary>
    [MaxLength(50)]
    public string? FirstName { get; set; }

    /// <summary>
    /// Last name
    /// </summary>
    [MaxLength(50)]
    public string? LastName { get; set; }

    /// <summary>
    /// Full name
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// Navigation property to user roles
    /// </summary>
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>
    /// Navigation property to user claims
    /// </summary>
    public virtual ICollection<UserClaim> UserClaims { get; set; } = new List<UserClaim>();

    /// <summary>
    /// Navigation property to user tokens
    /// </summary>
    public virtual ICollection<UserToken> UserTokens { get; set; } = new List<UserToken>();
}

/// <summary>
/// Role entity
/// </summary>
public class Role
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Role name (unique)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Normalized role name
    /// </summary>
    [MaxLength(50)]
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// Role description
    /// </summary>
    [MaxLength(200)]
    public string? Description { get; set; }

    /// <summary>
    /// Navigation property to user roles
    /// </summary>
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <summary>
    /// Navigation property to role claims
    /// </summary>
    public virtual ICollection<RoleClaim> RoleClaims { get; set; } = new List<RoleClaim>();
}

/// <summary>
/// User-Role mapping entity
/// </summary>
public class UserRole
{
    /// <summary>
    /// User ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Role ID
    /// </summary>
    public int RoleId { get; set; }

    /// <summary>
    /// Navigation property to user
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// Navigation property to role
    /// </summary>
    public virtual Role Role { get; set; } = null!;
}

/// <summary>
/// User claim entity
/// </summary>
public class UserClaim
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// User ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Claim type
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ClaimType { get; set; } = string.Empty;

    /// <summary>
    /// Claim value
    /// </summary>
    [MaxLength(500)]
    public string? ClaimValue { get; set; }

    /// <summary>
    /// Navigation property to user
    /// </summary>
    public virtual User User { get; set; } = null!;
}

/// <summary>
/// Role claim entity
/// </summary>
public class RoleClaim
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Role ID
    /// </summary>
    public int RoleId { get; set; }

    /// <summary>
    /// Claim type
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ClaimType { get; set; } = string.Empty;

    /// <summary>
    /// Claim value
    /// </summary>
    [MaxLength(500)]
    public string? ClaimValue { get; set; }

    /// <summary>
    /// Navigation property to role
    /// </summary>
    public virtual Role Role { get; set; } = null!;
}

/// <summary>
/// User token entity (for refresh tokens, etc.)
/// </summary>
public class UserToken
{
    /// <summary>
    /// User ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Login provider (e.g., "Local", "Google")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string LoginProvider { get; set; } = string.Empty;

    /// <summary>
    /// Token name (e.g., "RefreshToken", "AuthToken")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Token value
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Token expiration date
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Navigation property to user
    /// </summary>
    public virtual User User { get; set; } = null!;
}
