using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WasmMvcRuntime.Identity.Security;

namespace WasmMvcRuntime.Identity.Services;

/// <summary>
/// Service for managing user sessions.
/// Security model mirrors ASP.NET Core cookie authentication:
/// - All session data stays in Worker memory (server-side equivalent)
/// - Browser receives ONLY an HMAC-signed opaque token
/// - Sessions are bound to a fingerprint (User-Agent etc.)
/// - Concurrent session limit enforced per user
/// - State persisted to OPFS — survives page refresh and new tabs
/// </summary>
public interface ISessionStorageService
{
    Task SaveSessionAsync(SessionData session);
    Task<SessionData?> GetSessionAsync();
    Task RemoveSessionAsync();
    Task<bool> HasActiveSessionAsync();
}

/// <summary>
/// Hardened session storage with HMAC-signed tokens, fingerprint binding,
/// and concurrent session limits. State persisted to OPFS for durability.
/// </summary>
public class SessionStorageService : ISessionStorageService
{
    private const string TokenKey = "cepha_sid";
    private const int MaxSessionsPerUser = 3;

    // Runtime key — persisted to OPFS, restored on startup
    private static byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);

    // Server-side session store (Worker memory, persisted to OPFS)
    private static readonly Dictionary<string, SessionEntry> _sessions = new();
    private static readonly object _lock = new();

    // Active IdentityAtom — the binary entity bridging both universes
    private static IdentityAtom? _activeAtom;

    // Dirty flag — set when state changes, cleared after persist
    private static bool _dirty;

    // Current client fingerprint (set once on init from Worker context)
    private static string? _clientFingerprint;

    /// <summary>
    /// Set client fingerprint from navigation User-Agent or similar context.
    /// Called once during app init. Sessions are bound to this fingerprint.
    /// </summary>
    public static void SetClientFingerprint(string fingerprint)
    {
        _clientFingerprint ??= ComputeHash(fingerprint);
    }

    /// <summary>True when session state changed since last persist.</summary>
    public static bool IsDirty => _dirty;

    /// <summary>
    /// 🔬 Current IdentityAtom — the binary entity that identity components depend on.
    /// Null when no active session (guest state).
    /// </summary>
    public static IdentityAtom? ActiveAtom => _activeAtom;

    /// <summary>Get the current HMAC key (for atom operations).</summary>
    internal static byte[] HmacKey => _hmacKey;

    /// <summary>
    /// 🔬 Reforge the active atom for a new page path.
    /// Keeps the HMAC key encapsulated within this service.
    /// </summary>
    public static void ReforgeAtomForPage(string pagePath)
    {
        _activeAtom?.ReforgeForPage(pagePath, _hmacKey);
    }

    public Task SaveSessionAsync(SessionData session)
    {
        var tokenId = GenerateTokenId();
        var fingerprint = _clientFingerprint ?? "unknown";

        // Sign the token: HMAC(tokenId + fingerprint)
        var signedToken = SignToken(tokenId, fingerprint);
        session.Token = signedToken;

        var entry = new SessionEntry
        {
            Data = session,
            TokenId = tokenId,
            Fingerprint = fingerprint,
            CreatedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            // Enforce concurrent session limit per user
            var userSessions = _sessions
                .Where(kv => kv.Value.Data.UserId == session.UserId)
                .OrderBy(kv => kv.Value.CreatedAt)
                .ToList();

            while (userSessions.Count >= MaxSessionsPerUser)
            {
                _sessions.Remove(userSessions[0].Key);
                userSessions.RemoveAt(0);
            }

            // Purge all expired sessions
            var expired = _sessions
                .Where(kv => kv.Value.Data.ExpiresAt < DateTime.UtcNow)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in expired) _sessions.Remove(key);

            _sessions[tokenId] = entry;
            _dirty = true;

            // 🔬 Forge IdentityAtom — binary entity split across two universes
            _activeAtom = IdentityAtom.Forge(
                pagePath: "/",
                userId: session.UserId,
                fingerprint: fingerprint,
                sessionCreatedAt: session.CreatedAt,
                roles: session.Roles,
                hmacKey: _hmacKey);
        }

        // Only the HMAC-signed opaque token goes to the browser
        StorageInterop.SetItem(TokenKey, signedToken);
        return Task.CompletedTask;
    }

    public Task<SessionData?> GetSessionAsync()
    {
        try
        {
            var signedToken = StorageInterop.GetItem(TokenKey);
            if (string.IsNullOrEmpty(signedToken))
                return Task.FromResult<SessionData?>(null);

            var tokenId = ExtractAndVerify(signedToken);
            if (tokenId == null)
            {
                StorageInterop.RemoveItem(TokenKey);
                return Task.FromResult<SessionData?>(null);
            }

            lock (_lock)
            {
                if (!_sessions.TryGetValue(tokenId, out var entry))
                    return Task.FromResult<SessionData?>(null);

                var currentFingerprint = _clientFingerprint ?? "unknown";
                if (entry.Fingerprint != currentFingerprint)
                {
                    _sessions.Remove(tokenId);
                    StorageInterop.RemoveItem(TokenKey);
                    _dirty = true;
                    return Task.FromResult<SessionData?>(null);
                }

                if (entry.Data.ExpiresAt < DateTime.UtcNow)
                {
                    _sessions.Remove(tokenId);
                    StorageInterop.RemoveItem(TokenKey);
                    _dirty = true;
                    return Task.FromResult<SessionData?>(null);
                }

                return Task.FromResult<SessionData?>(entry.Data);
            }
        }
        catch
        {
            return Task.FromResult<SessionData?>(null);
        }
    }

    public Task RemoveSessionAsync()
    {
        try
        {
            var signedToken = StorageInterop.GetItem(TokenKey);
            if (!string.IsNullOrEmpty(signedToken))
            {
                var tokenId = ExtractAndVerify(signedToken);
                if (tokenId != null)
                {
                    lock (_lock) { _sessions.Remove(tokenId); _dirty = true; }
                }
            }
            StorageInterop.RemoveItem(TokenKey);
            _activeAtom = null; // Decohere atom on logout
        }
        catch { }
        return Task.CompletedTask;
    }

    public async Task<bool> HasActiveSessionAsync()
    {
        var session = await GetSessionAsync();
        return session != null;
    }

    // ─── State Persistence (OPFS) ────────────────────────────

    /// <summary>
    /// Serialize session state (HMAC key + all sessions) to JSON.
    /// Called by CephaApp to persist to OPFS after requests.
    /// </summary>
    public static string SerializeState()
    {
        lock (_lock)
        {
            _dirty = false;
            var state = new PersistedState
            {
                HmacKey = Convert.ToBase64String(_hmacKey),
                AtomObservable = _activeAtom?.SerializeObservable(),
                AtomEntangled = _activeAtom?.SerializeEntangled(),
                Sessions = _sessions
                    .Where(kv => kv.Value.Data.ExpiresAt > DateTime.UtcNow)
                    .ToDictionary(kv => kv.Key, kv => new PersistedSession
                    {
                        TokenId = kv.Value.TokenId,
                        Fingerprint = kv.Value.Fingerprint,
                        CreatedAt = kv.Value.CreatedAt,
                        Token = kv.Value.Data.Token,
                        UserId = kv.Value.Data.UserId,
                        UserName = kv.Value.Data.UserName,
                        Email = kv.Value.Data.Email,
                        FullName = kv.Value.Data.FullName,
                        Roles = kv.Value.Data.Roles,
                        SessionCreatedAt = kv.Value.Data.CreatedAt,
                        ExpiresAt = kv.Value.Data.ExpiresAt,
                        RememberMe = kv.Value.Data.RememberMe,
                        SecurityStamp = kv.Value.Data.SecurityStamp,
                        Claims = kv.Value.Data.Claims
                    })
            };
            return JsonSerializer.Serialize(state);
        }
    }

    /// <summary>
    /// Restore session state from JSON. Called by CephaApp on startup.
    /// Returns true if state was successfully restored.
    /// </summary>
    public static bool RestoreState(string? json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state == null) return false;

            lock (_lock)
            {
                // Restore HMAC key (critical — tokens signed with this key)
                _hmacKey = Convert.FromBase64String(state.HmacKey);

                _sessions.Clear();
                var now = DateTime.UtcNow;
                foreach (var kv in state.Sessions)
                {
                    if (kv.Value.ExpiresAt <= now) continue; // skip expired

                    var sessionData = new SessionData
                    {
                        Token = kv.Value.Token,
                        UserId = kv.Value.UserId,
                        UserName = kv.Value.UserName,
                        Email = kv.Value.Email,
                        FullName = kv.Value.FullName,
                        Roles = kv.Value.Roles,
                        CreatedAt = kv.Value.SessionCreatedAt,
                        ExpiresAt = kv.Value.ExpiresAt,
                        RememberMe = kv.Value.RememberMe,
                        SecurityStamp = kv.Value.SecurityStamp,
                        Claims = kv.Value.Claims
                    };

                    _sessions[kv.Key] = new SessionEntry
                    {
                        Data = sessionData,
                        TokenId = kv.Value.TokenId,
                        Fingerprint = kv.Value.Fingerprint,
                        CreatedAt = kv.Value.CreatedAt
                    };
                }
            }

            // Reassemble IdentityAtom from both universes
            RestoreAtom(state);

            // 🔑 Critical: sync Worker's in-memory storage with restored session.
            // Each Worker has its own _storage dict — after OPFS restore, we must
            // set the active token so GetSessionAsync() can find it.
            SyncStorageFromRestoredSessions();

            return _sessions.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reassemble IdentityAtom from persisted halves after restore.
    /// Called internally by RestoreState when atom data is present.
    /// </summary>
    private static void RestoreAtom(PersistedState state)
    {
        if (string.IsNullOrEmpty(state.AtomObservable) || string.IsNullOrEmpty(state.AtomEntangled))
        {
            _activeAtom = null;
            return;
        }
        try
        {
            var obs = Convert.FromBase64String(state.AtomObservable);
            var ent = Convert.FromBase64String(state.AtomEntangled);
            _activeAtom = IdentityAtom.Reassemble(obs, ent);
        }
        catch { _activeAtom = null; }
    }

    /// <summary>
    /// After restoring sessions from OPFS, find the most recent active session
    /// matching the current fingerprint and set its token in the Worker's
    /// in-memory storage. Without this, GetSessionAsync() returns null because
    /// the Worker's _storage dict doesn't have the token after cross-tab sync.
    /// On logout (no matching session), the token is removed from storage.
    /// </summary>
    private static void SyncStorageFromRestoredSessions()
    {
        try
        {
            var currentFp = _clientFingerprint ?? "unknown";
            var matching = _sessions.Values
                .Where(s => s.Fingerprint == currentFp && s.Data.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();

            if (matching != null)
            {
                // Set the active session's signed token in Worker storage
                StorageInterop.SetItem(TokenKey, matching.Data.Token);
            }
            else
            {
                // No active session for this fingerprint — clear token (logout sync)
                StorageInterop.RemoveItem(TokenKey);
            }
        }
        catch { /* Storage sync best-effort */ }
    }

    // ─── Cryptographic Operations ────────────────────────────

    private static string GenerateTokenId()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string SignToken(string tokenId, string fingerprint)
    {
        var payload = Encoding.UTF8.GetBytes(tokenId + "|" + fingerprint);
        var signature = HMACSHA256.HashData(_hmacKey, payload);
        var sig64 = Convert.ToBase64String(signature)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        return $"{tokenId}.{sig64}";
    }

    private static string? ExtractAndVerify(string signedToken)
    {
        var dotIndex = signedToken.LastIndexOf('.');
        if (dotIndex < 1) return null;

        var tokenId = signedToken[..dotIndex];
        var providedSig = signedToken[(dotIndex + 1)..];
        var fingerprint = _clientFingerprint ?? "unknown";

        var payload = Encoding.UTF8.GetBytes(tokenId + "|" + fingerprint);
        var expectedSig = Convert.ToBase64String(HMACSHA256.HashData(_hmacKey, payload))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSig),
            Encoding.UTF8.GetBytes(providedSig)))
        {
            return null;
        }

        return tokenId;
    }

    private static string ComputeHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    // ─── Internal Types ──────────────────────────────────────

    private sealed class SessionEntry
    {
        public required SessionData Data { get; init; }
        public required string TokenId { get; init; }
        public required string Fingerprint { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    private sealed class PersistedState
    {
        public string HmacKey { get; set; } = "";
        public Dictionary<string, PersistedSession> Sessions { get; set; } = new();
        /// <summary>Observable half of IdentityAtom (Worker memory side)</summary>
        public string? AtomObservable { get; set; }
        /// <summary>Entangled half of IdentityAtom (OPFS / parallel universe side)</summary>
        public string? AtomEntangled { get; set; }
    }

    private sealed class PersistedSession
    {
        public string TokenId { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Token { get; set; } = "";
        public int UserId { get; set; }
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
        public string FullName { get; set; } = "";
        public List<string> Roles { get; set; } = new();
        public DateTime SessionCreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool RememberMe { get; set; }
        public string SecurityStamp { get; set; } = "";
        public Dictionary<string, string> Claims { get; set; } = new();
    }
}

/// <summary>
/// JSImport bindings — Worker's in-memory storage proxy
/// </summary>
internal static partial class StorageInterop
{
    [JSImport("storage.getItem", "main.js")]
    internal static partial string? GetItem(string key);

    [JSImport("storage.setItem", "main.js")]
    internal static partial void SetItem(string key, string value);

    [JSImport("storage.removeItem", "main.js")]
    internal static partial void RemoveItem(string key);
}

/// <summary>
/// Session data — stored ONLY in Worker memory, never serialized to browser.
/// The browser only sees an HMAC-signed opaque token.
/// </summary>
public class SessionData
{
    /// <summary>Signed token (tokenId.hmacSignature) — only value sent to browser</summary>
    internal string Token { get; set; } = string.Empty;

    /// <summary>User ID</summary>
    public int UserId { get; set; }

    /// <summary>Username</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Email</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Full name</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>User roles</summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>Session creation time</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Session expiration time</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Remember me flag</summary>
    public bool RememberMe { get; set; }

    /// <summary>Security stamp (for validation)</summary>
    public string SecurityStamp { get; set; } = string.Empty;

    /// <summary>Additional user data</summary>
    public Dictionary<string, string> Claims { get; set; } = new();
}
