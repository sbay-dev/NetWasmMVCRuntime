using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Cepha;

/// <summary>
/// JavaScript interop — [JSImport] bindings to browser APIs.
/// Provided by NetWasmMvc.SDK. Extend by creating your own partial class.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class JsInterop
{
    // ─── DOM Manipulation ─────────────────────────────────────

    [JSImport("dom.setInnerHTML", "main.js")]
    public static partial void SetInnerHTML(string selector, string html);

    [JSImport("dom.setInnerText", "main.js")]
    public static partial void SetInnerText(string selector, string content);

    [JSImport("dom.getInnerText", "main.js")]
    public static partial string? GetInnerText(string selector);

    [JSImport("dom.setAttribute", "main.js")]
    public static partial void SetAttribute(string selector, string attribute, string value);

    [JSImport("dom.addClass", "main.js")]
    public static partial void AddClass(string selector, string className);

    [JSImport("dom.removeClass", "main.js")]
    public static partial void RemoveClass(string selector, string className);

    [JSImport("dom.show", "main.js")]
    public static partial void Show(string selector);

    [JSImport("dom.hide", "main.js")]
    public static partial void Hide(string selector);

    // ─── localStorage ─────────────────────────────────────────

    [JSImport("storage.getItem", "main.js")]
    public static partial string? StorageGetItem(string key);

    [JSImport("storage.setItem", "main.js")]
    public static partial void StorageSetItem(string key, string value);

    [JSImport("storage.removeItem", "main.js")]
    public static partial void StorageRemoveItem(string key);

    // ─── Console ──────────────────────────────────────────────

    [JSImport("globalThis.console.log")]
    public static partial void ConsoleLog(string message);

    [JSImport("globalThis.console.warn")]
    public static partial void ConsoleWarn(string message);

    [JSImport("globalThis.console.error")]
    public static partial void ConsoleError(string message);

    // ─── Navigation ───────────────────────────────────────────

    [JSImport("navigation.getPath", "main.js")]
    public static partial string GetCurrentPath();

    [JSImport("navigation.getFingerprint", "main.js")]
    public static partial string GetFingerprint();

    [JSImport("navigation.pushState", "main.js")]
    public static partial void PushState(string path);

    [JSImport("navigation.navigateTo", "main.js")]
    public static partial Task NavigateTo(string path);

    // ─── File Download ────────────────────────────────────────

    [JSImport("fileOps.downloadFile", "main.js")]
    public static partial void DownloadFile(string filename, string base64Content, string contentType);

    // ─── SignalR Hub Event Dispatch ───────────────────────────

    [JSImport("signalr.dispatchHubEvent", "main.js")]
    public static partial void DispatchHubEvent(string hubName, string method, string? connectionId, string argsJson);

    // ─── CephaKit Server Discovery ───────────────────────────

    [JSImport("cephakit.start", "main.js")]
    public static partial void StartCephaKit(int port);

    // ─── OPFS Data Bridge (via Runtime Worker → Main → Data Worker) ─

    [JSImport("opfs.write", "main.js")]
    public static partial Task OpfsWrite(string path, string data);

    [JSImport("opfs.read", "main.js")]
    public static partial Task<string?> OpfsRead(string path);

    // ─── Cross-Tab Auth Broadcast ──────────────────────────────

    [JSImport("auth.broadcast", "main.js")]
    public static partial void BroadcastAuthChange(string action);

    // ─── DOM Streaming (Plasma Source → Display Surface) ──────

    [JSImport("dom.streamStart", "main.js")]
    public static partial void StreamStart(string selector);

    [JSImport("dom.streamAppend", "main.js")]
    public static partial void StreamAppend(string selector, string html);

    [JSImport("dom.streamEnd", "main.js")]
    public static partial void StreamEnd(string selector);

    [JSImport("cephaDb.restore", "main.js")]
    public static partial Task<string?> RestoreDbFromOPFS();

    [JSImport("cephaDb.persist", "main.js")]
    public static partial Task PersistDbToOPFS(string base64Data);
}
