namespace WasmMvcRuntime.Identity.Services;

/// <summary>
/// Atomic probes — centralized control panel for intercepting identity events.
/// Injected as a Singleton and fires lambdas on every identity state change.
/// No Controller or View needs to know the details — the trigger is centralized and decisive.
/// </summary>
public class IdentityTriggers
{
    /// <summary>
    /// Fired on successful sign-in.
    /// Parameters: (userId, userName, roles[])
    /// </summary>
    public Func<string, string, string[], Task>? OnSignedIn { get; set; }

    /// <summary>
    /// Fired on sign-out.
    /// Parameters: (userId, userName)
    /// </summary>
    public Func<string, string, Task>? OnSignedOut { get; set; }

    /// <summary>
    /// Fired on sign-in failure.
    /// Parameters: (userName, reason)
    /// </summary>
    public Action<string, string>? OnSignInFailed { get; set; }

    /// <summary>
    /// Fired when an account is locked due to failed attempts.
    /// Parameters: (userName, lockoutEnd)
    /// </summary>
    public Action<string, DateTimeOffset>? OnLockedOut { get; set; }

    internal async Task FireSignedInAsync(string userId, string userName, string[] roles)
    {
        if (OnSignedIn != null)
            await OnSignedIn(userId, userName, roles);
    }

    internal async Task FireSignedOutAsync(string userId, string userName)
    {
        if (OnSignedOut != null)
            await OnSignedOut(userId, userName);
    }

    internal void FireSignInFailed(string userName, string reason)
        => OnSignInFailed?.Invoke(userName, reason);

    internal void FireLockedOut(string userName, DateTimeOffset lockoutEnd)
        => OnLockedOut?.Invoke(userName, lockoutEnd);
}
