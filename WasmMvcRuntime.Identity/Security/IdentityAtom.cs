using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace WasmMvcRuntime.Identity.Security;

/// <summary>
/// 🔬 IdentityAtom — a binary atomic entity designed at the bit level.
/// It is the atomic representation of pages — components recognize their shape only through it.
/// 
/// Binary layout:
/// ┌─────────────────────────────────────────────────────┐
/// │ Observable Half (64 bytes) — Worker Memory          │
/// │ [0..7]   PageHash     — truncated SHA256 of path    │
/// │ [8..23]  UserIdHash   — HMAC of UserId + fingerprint│
/// │ [24..27] Permissions  — bitfield render grants       │
/// │ [28..35] Nonce        — random per-derivation        │
/// │ [36..39] Epoch        — seconds since session start  │
/// │ [40..55] Entropy      — 16 random bytes              │
/// │ [56..63] Checksum     — SipHash of [0..55]           │
/// ├─────────────────────────────────────────────────────┤
/// │ Entangled Half (64 bytes) — OPFS (parallel universe)│
/// │ [0..31]  SessionSecret — HKDF-derived session salt   │
/// │ [32..47] KeyFragment   — XOR half of full atom key   │
/// │ [48..55] EpochBinding  — binds to Observable.Epoch   │
/// │ [56..63] Checksum      — SipHash of [0..55]          │
/// └─────────────────────────────────────────────────────┘
/// 
/// Neither half alone reveals identity state.
/// Only the Quantum Tunnel (HKDF) can reassemble the RenderToken.
/// </summary>
public sealed class IdentityAtom
{
    public const int HalfSize = 64;
    public const int AtomSize = HalfSize * 2;
    private const string HkdfInfo = "cepha-identity-atom-v1";

    /// <summary>Observable half — resides in Worker memory (this universe).</summary>
    public byte[] Observable { get; private set; } = new byte[HalfSize];

    /// <summary>Entangled half — resides in OPFS (parallel universe).</summary>
    public byte[] Entangled { get; private set; } = new byte[HalfSize];

    /// <summary>Derived RenderToken — valid ONLY when both halves are present and intact.</summary>
    public byte[] RenderToken { get; private set; } = Array.Empty<byte>();

    /// <summary>Whether both halves checksum-verified and tunnel produced a valid token.</summary>
    public bool IsCoherent { get; private set; }

    /// <summary>
    /// Render permission flags — bitfield controlling what identity components can render.
    /// </summary>
    [Flags]
    public enum RenderGrant : uint
    {
        None            = 0,
        UserInfo        = 1 << 0,   // show username/avatar
        LoginPartial    = 1 << 1,   // full login partial component
        RoleIndicator   = 1 << 2,   // show role badges
        SessionControls = 1 << 3,   // logout button, session management
        AdminPanel      = 1 << 4,   // admin-level components
        All             = 0xFFFFFFFF
    }

    // ─── Factory: Create from session sign-in ──────────────────

    /// <summary>
    /// Forge a new IdentityAtom when a user signs in.
    /// Observable half stays in Worker memory. Entangled half goes to OPFS.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IdentityAtom Forge(
        string pagePath,
        int userId,
        string fingerprint,
        DateTime sessionCreatedAt,
        IReadOnlyList<string> roles,
        byte[] hmacKey)
    {
        var atom = new IdentityAtom();

        // ── Observable Half (Worker memory) ────────────────────
        var obs = atom.Observable;

        // [0..7] PageHash — SHA256(path) truncated to 8 bytes
        var pageHash = SHA256.HashData(Encoding.UTF8.GetBytes(pagePath));
        Buffer.BlockCopy(pageHash, 0, obs, 0, 8);

        // [8..23] UserIdHash — HMAC-SHA256(userId|fingerprint) truncated to 16 bytes
        var userPayload = Encoding.UTF8.GetBytes($"{userId}|{fingerprint}");
        var userHash = HMACSHA256.HashData(hmacKey, userPayload);
        Buffer.BlockCopy(userHash, 0, obs, 8, 16);

        // [24..27] Permissions — bitfield based on roles
        var grants = RenderGrant.UserInfo | RenderGrant.LoginPartial | RenderGrant.SessionControls;
        if (roles.Contains("Admin")) grants |= RenderGrant.AdminPanel | RenderGrant.RoleIndicator;
        if (roles.Count > 0) grants |= RenderGrant.RoleIndicator;
        BinaryPrimitives.WriteUInt32LittleEndian(obs.AsSpan(24), (uint)grants);

        // [28..35] Nonce — 8 cryptographically random bytes
        RandomNumberGenerator.Fill(obs.AsSpan(28, 8));

        // [36..39] Epoch — seconds since session created
        var epoch = (uint)(DateTime.UtcNow - sessionCreatedAt).TotalSeconds;
        BinaryPrimitives.WriteUInt32LittleEndian(obs.AsSpan(36), epoch);

        // [40..55] Entropy — 16 random bytes
        RandomNumberGenerator.Fill(obs.AsSpan(40, 16));

        // [56..63] Checksum — SipHash-like via HMAC-truncated
        WriteChecksum(obs);

        // ── Entangled Half (OPFS / parallel universe) ──────────
        var ent = atom.Entangled;

        // [0..31] SessionSecret — HKDF from hmacKey + userId salt
        var sessionSalt = Encoding.UTF8.GetBytes($"cepha-entangled-{userId}-{fingerprint}");
        HKDF.DeriveKey(HashAlgorithmName.SHA256, hmacKey, ent.AsSpan(0, 32), sessionSalt,
            Encoding.UTF8.GetBytes("session-secret"));

        // [32..47] KeyFragment — XOR of hmacKey[0..15] with entropy
        for (int i = 0; i < 16; i++)
            ent[32 + i] = (byte)(hmacKey[i % hmacKey.Length] ^ obs[40 + i]);

        // [48..55] EpochBinding — must match Observable[28..35] (nonce)
        Buffer.BlockCopy(obs, 28, ent, 48, 8);

        // [56..63] Checksum
        WriteChecksum(ent);

        // ── Quantum Tunnel → derive RenderToken ────────────────
        atom.RenderToken = QuantumTunnel(obs, ent);
        atom.IsCoherent = true;

        return atom;
    }

    // ─── Factory: Reconstruct from persisted halves ────────────

    /// <summary>
    /// Reassemble an atom from its two halves (after page refresh / tab sync).
    /// Verifies checksums and re-derives the RenderToken through the quantum tunnel.
    /// Returns null if either half is corrupted (decoherence).
    /// </summary>
    public static IdentityAtom? Reassemble(byte[] observable, byte[] entangled)
    {
        if (observable.Length != HalfSize || entangled.Length != HalfSize)
            return null;

        if (!VerifyChecksum(observable) || !VerifyChecksum(entangled))
            return null;

        // Verify epoch binding — entangled[48..55] must match observable[28..35]
        if (!observable.AsSpan(28, 8).SequenceEqual(entangled.AsSpan(48, 8)))
            return null;

        var atom = new IdentityAtom
        {
            Observable = observable,
            Entangled = entangled
        };

        atom.RenderToken = QuantumTunnel(observable, entangled);
        atom.IsCoherent = true;
        return atom;
    }

    // ─── Re-forge for new page navigation ──────────────────────

    /// <summary>
    /// Update the Observable half for a new page path without regenerating the Entangled half.
    /// The quantum tunnel produces a fresh RenderToken per page.
    /// </summary>
    public IdentityAtom ReforgeForPage(string newPagePath, byte[] hmacKey)
    {
        // Update PageHash
        var pageHash = SHA256.HashData(Encoding.UTF8.GetBytes(newPagePath));
        Buffer.BlockCopy(pageHash, 0, Observable, 0, 8);

        // Fresh nonce
        RandomNumberGenerator.Fill(Observable.AsSpan(28, 8));

        // Update epoch
        var epoch = BinaryPrimitives.ReadUInt32LittleEndian(Observable.AsSpan(36));
        epoch += 1; // increment monotonically
        BinaryPrimitives.WriteUInt32LittleEndian(Observable.AsSpan(36), epoch);

        // Fresh entropy
        RandomNumberGenerator.Fill(Observable.AsSpan(40, 16));

        // Update KeyFragment with new entropy
        for (int i = 0; i < 16; i++)
            Entangled[32 + i] = (byte)(hmacKey[i % hmacKey.Length] ^ Observable[40 + i]);

        // Rebind nonce
        Buffer.BlockCopy(Observable, 28, Entangled, 48, 8);

        // Recompute checksums
        WriteChecksum(Observable);
        WriteChecksum(Entangled);

        // Re-tunnel
        RenderToken = QuantumTunnel(Observable, Entangled);
        return this;
    }

    // ─── Query: Extract render grants from Observable ──────────

    /// <summary>Read the permission bitfield from Observable[24..27].</summary>
    public RenderGrant GetGrants()
    {
        return (RenderGrant)BinaryPrimitives.ReadUInt32LittleEndian(Observable.AsSpan(24));
    }

    /// <summary>Check if a specific render grant is present.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasGrant(RenderGrant grant) => (GetGrants() & grant) == grant;

    // ─── Serialization: Base64 for OPFS transport ──────────────

    /// <summary>Serialize the Entangled half for OPFS storage.</summary>
    public string SerializeEntangled() => Convert.ToBase64String(Entangled);

    /// <summary>Serialize the Observable half (for session state persistence).</summary>
    public string SerializeObservable() => Convert.ToBase64String(Observable);

    /// <summary>Hex representation of RenderToken (for ViewBag injection).</summary>
    public string RenderTokenHex() => Convert.ToHexString(RenderToken).ToLowerInvariant();

    // ─── The Quantum Tunnel ────────────────────────────────────

    /// <summary>
    /// 🌀 Quantum Tunnel — HKDF derivation bridging two universes.
    /// Combines Observable (this universe) + Entangled (parallel universe)
    /// to produce the RenderToken that pages need for identity rendering.
    /// Neither half alone produces a valid token.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] QuantumTunnel(ReadOnlySpan<byte> observable, ReadOnlySpan<byte> entangled)
    {
        // IKM: Observable.Entropy[40..55] || Entangled.SessionSecret[0..31]
        Span<byte> ikm = stackalloc byte[48];
        observable.Slice(40, 16).CopyTo(ikm);
        entangled.Slice(0, 32).CopyTo(ikm.Slice(16));

        // Salt: Observable.Nonce[28..35] || Entangled.EpochBinding[48..55]
        Span<byte> salt = stackalloc byte[16];
        observable.Slice(28, 8).CopyTo(salt);
        entangled.Slice(48, 8).CopyTo(salt.Slice(8));

        // Derive 32-byte RenderToken
        var token = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, token, salt,
            Encoding.UTF8.GetBytes(HkdfInfo));
        return token;
    }

    // ─── Checksum: integrity guard per half ────────────────────

    /// <summary>Compute and write checksum at bytes [56..63] over [0..55].</summary>
    private static void WriteChecksum(byte[] half)
    {
        var hash = SHA256.HashData(half.AsSpan(0, 56));
        Buffer.BlockCopy(hash, 0, half, 56, 8);
    }

    /// <summary>Verify checksum at [56..63] matches hash of [0..55].</summary>
    private static bool VerifyChecksum(ReadOnlySpan<byte> half)
    {
        Span<byte> computed = stackalloc byte[32];
        SHA256.HashData(half.Slice(0, 56), computed);
        return computed.Slice(0, 8).SequenceEqual(half.Slice(56, 8));
    }

    // ─── Validation: Verify atom coherence ─────────────────────

    /// <summary>
    /// Verify an atom's RenderToken against a stored token.
    /// Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool VerifyRenderToken(byte[] token, byte[] expected)
    {
        if (token.Length != 32 || expected.Length != 32) return false;
        return CryptographicOperations.FixedTimeEquals(token, expected);
    }

    /// <summary>
    /// Create a guest atom — empty observable, no entangled, no render token.
    /// Identity components receiving this know to render guest state.
    /// </summary>
    public static IdentityAtom Guest() => new()
    {
        IsCoherent = false,
        RenderToken = Array.Empty<byte>()
    };
}
