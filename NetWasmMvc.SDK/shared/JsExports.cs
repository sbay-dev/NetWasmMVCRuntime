using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Cepha;

/// <summary>
/// JavaScript exports — [JSExport] methods callable from main.js.
/// Provided by NetWasmMvc.SDK. Register your handlers in Program.cs.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class JsExports
{
    private static Func<string, Task>? _navigateHandler;
    private static Func<string, string?, Task>? _formSubmitHandler;
    private static Func<string, Task<string>>? _fetchRouteHandler;
    private static Func<string, string, string, string?, Task<string?>>? _hubInvokeHandler;
    private static Func<string, Task<string>>? _hubConnectHandler;
    private static Func<string, string, Task>? _hubDisconnectHandler;

    // ─── Handler Registration ─────────────────────────────────

    public static void RegisterNavigateHandler(Func<string, Task> handler)
        => _navigateHandler = handler;

    public static void RegisterFormSubmitHandler(Func<string, string?, Task> handler)
        => _formSubmitHandler = handler;

    public static void RegisterFetchRouteHandler(Func<string, Task<string>> handler)
        => _fetchRouteHandler = handler;

    public static void RegisterHubInvokeHandler(Func<string, string, string, string?, Task<string?>> handler)
        => _hubInvokeHandler = handler;

    public static void RegisterHubConnectHandler(Func<string, Task<string>> handler)
        => _hubConnectHandler = handler;

    public static void RegisterHubDisconnectHandler(Func<string, string, Task> handler)
        => _hubDisconnectHandler = handler;

    // ─── JSExport Methods ─────────────────────────────────────

    [JSExport]
    public static async Task Navigate(string path)
    {
        if (_navigateHandler != null)
            await _navigateHandler(path);
        else
            JsInterop.ConsoleWarn($"[Cepha] No navigate handler registered for: {path}");
    }

    [JSExport]
    public static async Task SubmitForm(string action, string? formDataJson)
    {
        if (_formSubmitHandler != null)
            await _formSubmitHandler(action, formDataJson);
        else
            JsInterop.ConsoleWarn($"[Cepha] No form handler registered for: {action}");
    }

    [JSExport]
    public static async Task<string> FetchRoute(string path)
    {
        if (_fetchRouteHandler != null)
            return await _fetchRouteHandler(path);
        return "{\"error\": \"No fetch handler registered\"}";
    }

    [JSExport]
    public static async Task<string> HubConnect(string hubName)
    {
        if (_hubConnectHandler != null)
            return await _hubConnectHandler(hubName);
        return "";
    }

    [JSExport]
    public static async Task HubDisconnect(string hubName, string connectionId)
    {
        if (_hubDisconnectHandler != null)
            await _hubDisconnectHandler(hubName, connectionId);
    }

    [JSExport]
    public static async Task<string?> HubInvoke(string hubName, string method, string connectionId, string? argsJson)
    {
        if (_hubInvokeHandler != null)
            return await _hubInvokeHandler(hubName, method, connectionId, argsJson);
        return "{\"error\": \"No hub handler registered\"}";
    }

    // ─── Cross-Tab Auth Sync ──────────────────────────────────

    /// <summary>
    /// Called by other tabs via BroadcastChannel when auth state changes.
    /// Restores sessions from OPFS and re-renders current page.
    /// </summary>
    [JSExport]
    public static async Task SyncAuth()
    {
        await CephaApplication.RestoreSessionsFromOpfsAsync();
        var currentPath = JsInterop.GetCurrentPath();
        if (_navigateHandler != null)
            await _navigateHandler(currentPath);
    }

    // ─── Server-Side (CephaKit) Handler ───────────────────────

    private static Func<string, string, string?, string?, Task<string>>? _handleRequestHandler;

    public static void RegisterHandleRequestHandler(Func<string, string, string?, string?, Task<string>> handler)
        => _handleRequestHandler = handler;

    [JSExport]
    public static async Task<string> HandleRequest(string method, string path, string? headersJson, string? body)
    {
        if (_handleRequestHandler != null)
            return await _handleRequestHandler(method, path, headersJson, body);
        return "{\"statusCode\":503,\"body\":\"No request handler registered\"}";
    }
}
