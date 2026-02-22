# Security Model

WasmMvcRuntime implements a layered security model for client-side identity, session management, and data protection — all running within the browser's WebAssembly sandbox.

---

## Trust Model

**Important:** Client-side identity is designed for **offline-first** and **local-first** architectures where the trust boundary is the device itself. For server-authoritative authentication, combine this model with a server-side token validation layer.

---

## Identity System

### Password Security

| Property | Value |
|----------|-------|
| Algorithm | PBKDF2-SHA256 |
| Iterations | 100,000 |
| Salt | 16 bytes (random per user) |
| Hash | 32 bytes |
| Storage format | `base64(hash);base64(salt);iterations` |

### Account Lockout

- **Threshold:** 5 failed login attempts
- **Lockout duration:** 15 minutes
- **Reset:** Successful login resets the failed attempt counter
- **Active check:** Disabled accounts (`IsActive = false`) cannot sign in

### User Model

The `User` entity includes:
- `UserName` / `Email` (unique, normalized for lookups)
- `PasswordHash` (PBKDF2 format)
- `SecurityStamp` (changes on password reset — invalidates existing sessions)
- `LockoutEnd` / `AccessFailedCount` (lockout tracking)
- `TwoFactorEnabled` (reserved for future 2FA support)
- Roles via `UserRole` many-to-many relationship
- Claims via `UserClaim` collection

---

## Session Management

### Architecture

Sessions are managed entirely within the Web Worker — session state **never** enters `localStorage` or any main-thread-accessible storage.

```
Sign-In Flow:
1. SignInManager.PasswordSignInAsync(username, password)
2. Verify password (PBKDF2)
3. Check lockout status
4. Generate session token (32 random bytes → base64)
5. HMAC-sign the token with fingerprint binding
6. Store session in Worker memory
7. Persist signed token reference to Worker storage
8. Forge IdentityAtom (binary cryptographic entity)
```

### Token Security

| Property | Value |
|----------|-------|
| Token ID | 32 random bytes (base64-encoded) |
| Signing key | 32 bytes (HMAC-SHA256), persisted to OPFS |
| Signature | `HMAC-SHA256(tokenId \| fingerprint)` |
| Format | `tokenId.urlSafeBase64Signature` |
| Comparison | Constant-time (`CryptographicOperations.FixedTimeEquals`) |

### Fingerprint Binding

Every session is bound to the client's fingerprint (User-Agent hash). A token extracted from one browser will fail signature verification on another:

```
Token Verification:
1. Extract tokenId and signature from signed token
2. Compute expected signature: HMAC-SHA256(tokenId | currentFingerprint)
3. Constant-time compare expected vs provided signature
4. Look up tokenId in server-side (Worker memory) session store
5. Verify fingerprint matches stored fingerprint
6. Check expiration
```

### Concurrent Session Limits

- **Maximum:** 3 concurrent sessions per user
- **Enforcement:** When a 4th session is created, the oldest session is evicted
- **Expiration:** Persistent sessions = 30 days; non-persistent = 1 day

### Cross-Tab Synchronization

Authentication state is synchronized across browser tabs using `BroadcastChannel`:
- Login in one tab → all tabs re-render with authenticated state
- Logout in one tab → all tabs clear session and re-render

---

## IdentityAtom

A 128-byte cryptographic entity split across two storage locations. Neither half alone can produce a valid authentication token.

### Binary Layout

```
Observable Half (64 bytes) — Worker Memory
├─ [0..7]     PageHash      — truncated SHA256(path)
├─ [8..23]    UserIdHash    — HMAC-SHA256(userId|fingerprint), truncated
├─ [24..27]   Permissions   — RenderGrant bitfield
├─ [28..35]   Nonce         — 8 random bytes
├─ [36..39]   Epoch         — seconds since session start
├─ [40..55]   Entropy       — 16 random bytes
└─ [56..63]   Checksum      — SHA256([0..55]), truncated

Entangled Half (64 bytes) — OPFS Storage
├─ [0..31]    SessionSecret — HKDF-derived from HMAC key + user context
├─ [32..47]   KeyFragment   — XOR(hmacKey[0..15], Observable.Entropy)
├─ [48..55]   EpochBinding  — copy of Observable.Nonce (cross-reference)
└─ [56..63]   Checksum      — SHA256([0..55]), truncated
```

### RenderToken Derivation

The `RenderToken` is derived via HKDF-SHA256 using material from both halves:
- **IKM:** Observable.Entropy + Entangled.SessionSecret
- **Salt:** Observable.Nonce + Entangled.EpochBinding
- **Info:** `"cepha-identity-atom-v1"`
- **Output:** 32-byte token

### RenderGrant Permissions

```csharp
[Flags]
public enum RenderGrant : uint
{
    None            = 0,
    UserInfo        = 1 << 0,   // Username/avatar display
    LoginPartial    = 1 << 1,   // Full login component
    RoleIndicator   = 1 << 2,   // Role badges
    SessionControls = 1 << 3,   // Logout button
    AdminPanel      = 1 << 4,   // Admin UI access
    All             = 0xFFFFFFFF
}
```

---

## OPFS Persistence

### What is persisted

| Data | Storage | Encryption |
|------|---------|------------|
| SQLite databases | OPFS (binary snapshot) | None (browser sandbox) |
| HMAC signing key | OPFS (JSON) | None (Worker-only access) |
| Session entries | OPFS (JSON) | HMAC-signed tokens |
| IdentityAtom halves | Worker memory + OPFS | Binary layout with checksums |

### Persistence Strategy

- **Database:** Full binary snapshot after write operations (WAL checkpoint → OPFS)
- **Sessions:** Serialized after every sign-in/sign-out
- **Expired sessions:** Filtered out during serialization and restoration
- **HMAC key:** Persisted once, restored on every app boot (critical for token continuity)

---

## Threat Mitigations

| Threat | Mitigation |
|--------|------------|
| Token theft from browser storage | Tokens stored in Worker memory, not `localStorage`. Signing key never leaves Worker. |
| Session replay from another device | Fingerprint binding — token fails verification if User-Agent changes. |
| Brute-force password attacks | Account lockout (5 attempts / 15 min). PBKDF2 100K iterations. |
| Timing attacks on token comparison | `CryptographicOperations.FixedTimeEquals` for all signature comparisons. |
| Cross-tab session inconsistency | `BroadcastChannel` synchronizes auth state across tabs. |
| Session state loss on restart | HMAC key + sessions persisted to OPFS, restored on boot. |
| Token forgery | HMAC-SHA256 with 32-byte key. Without the key, forging a valid signature requires ~2^128 operations. |

---

## Source Files

| File | Purpose |
|------|---------|
| [`IdentityModels.cs`](../WasmMvcRuntime.Identity/Models/IdentityModels.cs) | User, Role, UserClaim entities |
| [`UserManager.cs`](../WasmMvcRuntime.Identity/Services/UserManager.cs) | User CRUD, password hashing, lockout |
| [`SignInManager.cs`](../WasmMvcRuntime.Identity/Services/SignInManager.cs) | Sign-in/sign-out flows |
| [`SessionStorageService.cs`](../WasmMvcRuntime.Identity/Services/SessionStorageService.cs) | HMAC tokens, fingerprint binding, OPFS persistence |
| [`IdentityAtom.cs`](../WasmMvcRuntime.Identity/Security/IdentityAtom.cs) | Binary cryptographic session entity |
| [`PasswordHasher.cs`](../WasmMvcRuntime.Identity/Services/PasswordHasher.cs) | PBKDF2-SHA256 implementation |
| [`IdentityServiceExtensions.cs`](../WasmMvcRuntime.Identity/Extensions/IdentityServiceExtensions.cs) | DI registration |
