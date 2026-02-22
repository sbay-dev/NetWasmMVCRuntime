using System.Collections.Concurrent;
using System.Text.Json;
using WasmMvcRuntime.Identity.Services;

namespace WasmMvcRuntime.Cepha.Services;

/// <summary>
/// Server-side in-memory session storage for the Cepha runtime.
/// Unlike the browser-based WasmSessionStorageService, this stores
/// sessions in a ConcurrentDictionary suitable for a server process.
/// Falls back to JS key-value storage for persistence across restarts.
/// </summary>
public class CephaSessionStorageService : ISessionStorageService
{
    private const string SessionKey = "WasmMvcRuntime_Session";
    private static readonly ConcurrentDictionary<string, SessionData> _sessions = new();

    public Task SaveSessionAsync(SessionData session)
    {
        var key = $"{SessionKey}_{session.UserId}";
        _sessions[key] = session;

        // Persist to JS storage layer
        try
        {
            var json = JsonSerializer.Serialize(session);
            CephaInterop.StorageSet(key, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CephaSession] Persist warning: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task<SessionData?> GetSessionAsync()
    {
        // Try in-memory first
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.ExpiresAt > DateTime.UtcNow)
                return Task.FromResult<SessionData?>(kvp.Value);
        }

        // Try JS storage fallback
        try
        {
            var json = CephaInterop.StorageGet(SessionKey);
            if (!string.IsNullOrEmpty(json))
            {
                var session = JsonSerializer.Deserialize<SessionData>(json);
                if (session != null && session.ExpiresAt > DateTime.UtcNow)
                {
                    var key = $"{SessionKey}_{session.UserId}";
                    _sessions[key] = session;
                    return Task.FromResult<SessionData?>(session);
                }
            }
        }
        catch { }

        return Task.FromResult<SessionData?>(null);
    }

    public Task RemoveSessionAsync()
    {
        _sessions.Clear();

        try
        {
            CephaInterop.StorageRemove(SessionKey);
        }
        catch { }

        return Task.CompletedTask;
    }

    public async Task<bool> HasActiveSessionAsync()
    {
        var session = await GetSessionAsync();
        return session != null && session.ExpiresAt > DateTime.UtcNow;
    }

    // ??? Server-specific: lookup by user ID ??????????????????

    /// <summary>
    /// Gets a session by user ID (server-side multi-user support).
    /// </summary>
    public SessionData? GetSessionByUserId(int userId)
    {
        var key = $"{SessionKey}_{userId}";
        return _sessions.TryGetValue(key, out var session) && session.ExpiresAt > DateTime.UtcNow
            ? session
            : null;
    }

    /// <summary>
    /// Removes expired sessions from the in-memory store.
    /// </summary>
    public int CleanupExpiredSessions()
    {
        var expired = _sessions
            .Where(kvp => kvp.Value.ExpiresAt <= DateTime.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
            _sessions.TryRemove(key, out _);

        return expired.Count;
    }
}
