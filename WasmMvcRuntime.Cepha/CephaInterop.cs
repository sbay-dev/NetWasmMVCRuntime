using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace WasmMvcRuntime.Cepha;

/// <summary>
/// JavaScript interop for the Cepha server runtime.
/// Imports are bound to functions exposed by main.mjs running on Node.js / Cloudflare Workers.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class CephaInterop
{
    // ??? Console ?????????????????????????????????????????????

    [JSImport("globalThis.console.log")]
    internal static partial void ConsoleLog(string message);

    [JSImport("globalThis.console.warn")]
    internal static partial void ConsoleWarn(string message);

    [JSImport("globalThis.console.error")]
    internal static partial void ConsoleError(string message);

    // ??? SSE: push an event to a connected SSE client ????????

    /// <summary>
    /// Sends an SSE event frame to the client identified by connectionId.
    /// JS side writes "event: {eventName}\ndata: {dataJson}\n\n" to the response stream.
    /// </summary>
    [JSImport("cepha.sseSend", "main.mjs")]
    internal static partial void SseSend(string connectionId, string eventName, string dataJson);

    /// <summary>
    /// Closes an SSE connection gracefully.
    /// </summary>
    [JSImport("cepha.sseClose", "main.mjs")]
    internal static partial void SseClose(string connectionId);

    // ??? SignalR: dispatch hub events to connected WS clients ?

    /// <summary>
    /// Dispatches a SignalR hub event to JavaScript for delivery over WebSocket.
    /// connectionId may be null/empty to broadcast.
    /// </summary>
    [JSImport("cepha.dispatchHubEvent", "main.mjs")]
    internal static partial void DispatchHubEvent(string hubName, string method, string? connectionId, string argsJson);

    // ??? HTTP response helpers ???????????????????????????????

    /// <summary>
    /// Sends an HTTP response back to the JS host.
    /// Called once per request after the MVC pipeline finishes.
    /// </summary>
    [JSImport("cepha.sendResponse", "main.mjs")]
    internal static partial void SendResponse(string requestId, int statusCode, string contentType, string body);

    /// <summary>
    /// Sends an HTTP response with custom headers serialized as JSON.
    /// </summary>
    [JSImport("cepha.sendResponseWithHeaders", "main.mjs")]
    internal static partial void SendResponseWithHeaders(string requestId, int statusCode, string contentType, string body, string headersJson);

    // ??? Key-Value storage (server-side in-memory or file) ???

    [JSImport("cepha.storageGet", "main.mjs")]
    internal static partial string? StorageGet(string key);

    [JSImport("cepha.storageSet", "main.mjs")]
    internal static partial void StorageSet(string key, string value);

    [JSImport("cepha.storageRemove", "main.mjs")]
    internal static partial void StorageRemove(string key);
}
