using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace WasmMvcRuntime.Client;

/// <summary>
/// ���� ������� �� JavaScript �������� [JSImport]/[JSExport]
/// ������ IJSRuntime �� Blazor ������ ������ �� WASM SDK
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class JsInterop
{
    // ??? DOM Manipulation ????????????????????????????????????

    [JSImport("dom.setInnerHTML", "main.js")]
    internal static partial void SetInnerHTML(string selector, string html);

    [JSImport("dom.setInnerText", "main.js")]
    internal static partial void SetInnerText(string selector, string content);

    [JSImport("dom.getInnerText", "main.js")]
    internal static partial string? GetInnerText(string selector);

    [JSImport("dom.setAttribute", "main.js")]
    internal static partial void SetAttribute(string selector, string attribute, string value);

    [JSImport("dom.addClass", "main.js")]
    internal static partial void AddClass(string selector, string className);

    [JSImport("dom.removeClass", "main.js")]
    internal static partial void RemoveClass(string selector, string className);

    [JSImport("dom.show", "main.js")]
    internal static partial void Show(string selector);

    [JSImport("dom.hide", "main.js")]
    internal static partial void Hide(string selector);

    // ??? localStorage ????????????????????????????????????????

    [JSImport("storage.getItem", "main.js")]
    internal static partial string? StorageGetItem(string key);

    [JSImport("storage.setItem", "main.js")]
    internal static partial void StorageSetItem(string key, string value);

    [JSImport("storage.removeItem", "main.js")]
    internal static partial void StorageRemoveItem(string key);

    // ??? Console ?????????????????????????????????????????????

    [JSImport("globalThis.console.log")]
    internal static partial void ConsoleLog(string message);

    [JSImport("globalThis.console.warn")]
    internal static partial void ConsoleWarn(string message);

    [JSImport("globalThis.console.error")]
    internal static partial void ConsoleError(string message);

    // ??? Navigation ??????????????????????????????????????????

    [JSImport("navigation.getPath", "main.js")]
    internal static partial string GetCurrentPath();

    [JSImport("navigation.getFingerprint", "main.js")]
    internal static partial string GetFingerprint();

    [JSImport("navigation.pushState", "main.js")]
    internal static partial void PushState(string path);

    // ??? File Download ???????????????????????????????????????

    [JSImport("fileOps.downloadFile", "main.js")]
    internal static partial void DownloadFile(string filename, string base64Content, string contentType);
    // ─── SignalR Hub Event Dispatch ───────────────────────────

    [JSImport("signalr.dispatchHubEvent", "main.js")]
    internal static partial void DispatchHubEvent(string hubName, string method, string? connectionId, string argsJson);
}
