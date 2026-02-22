using System.Runtime.Versioning;
using System.Text.Json;
using WasmMvcRuntime.Identity.Services;

namespace WasmMvcRuntime.Client.Services;

/// <summary>
///  ‰›Ì– SessionStorage »«” Œœ«„ [JSImport] »œ·« „‰ IJSRuntime
/// Ì⁄„· „⁄ „⁄„«—Ì… WASM SDK «·ÃœÌœ…
/// </summary>
[SupportedOSPlatform("browser")]
public class WasmSessionStorageService : ISessionStorageService
{
    private const string SessionKey = "WasmMvcRuntime_Session";

    public Task SaveSessionAsync(SessionData session)
    {
        try
        {
            var json = JsonSerializer.Serialize(session);
            JsInterop.StorageSetItem(SessionKey, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error saving session: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<SessionData?> GetSessionAsync()
    {
        try
        {
            var json = JsInterop.StorageGetItem(SessionKey);

            if (string.IsNullOrEmpty(json))
                return Task.FromResult<SessionData?>(null);

            var session = JsonSerializer.Deserialize<SessionData>(json);

            if (session != null && session.ExpiresAt < DateTime.UtcNow)
            {
                RemoveSessionAsync();
                return Task.FromResult<SessionData?>(null);
            }

            return Task.FromResult(session);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error getting session: {ex.Message}");
            return Task.FromResult<SessionData?>(null);
        }
    }

    public Task RemoveSessionAsync()
    {
        try
        {
            JsInterop.StorageRemoveItem(SessionKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error removing session: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task<bool> HasActiveSessionAsync()
    {
        var session = await GetSessionAsync();
        return session != null && session.ExpiresAt > DateTime.UtcNow;
    }
}
