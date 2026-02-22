using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace WasmMvcRuntime.Client;

/// <summary>
/// Functions exported to JavaScript, called from main.js.
/// Used by the new application architecture.
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

    /// <summary>
    /// Register the navigation handler.
    /// </summary>
    public static void RegisterNavigateHandler(Func<string, Task> handler)
    {
        _navigateHandler = handler;
    }

    /// <summary>
    /// Register the form submit handler.
    /// </summary>
    public static void RegisterFormSubmitHandler(Func<string, string?, Task> handler)
    {
        _formSubmitHandler = handler;
    }

    /// <summary>
    /// Register the fetch route handler (returns the response as text without modifying the DOM).
    /// </summary>
    public static void RegisterFetchRouteHandler(Func<string, Task<string>> handler)
    {
        _fetchRouteHandler = handler;
    }

    /// <summary>
    /// Called from JavaScript when navigating to a page via the navigation panel or bar.
    /// </summary>
    [JSExport]
    public static async Task Navigate(string path)
    {
        if (_navigateHandler != null)
        {
            await _navigateHandler(path);
        }
        else
        {
            JsInterop.ConsoleWarn($"[WasmMvc] No navigate handler registered for path: {path}");
        }
    }

    /// <summary>
    /// Called from JavaScript when submitting a form.
    /// </summary>
    [JSExport]
    public static async Task SubmitForm(string action, string? formDataJson)
    {
        if (_formSubmitHandler != null)
        {
            await _formSubmitHandler(action, formDataJson);
        }
        else
        {
            JsInterop.ConsoleWarn($"[WasmMvc] No form submit handler registered for action: {action}");
        }
    }

    /// <summary>
    /// Processes a route and returns the response as JSON text without modifying the page.
    /// </summary>
    [JSExport]
    public static async Task<string> FetchRoute(string path)
    {
        if (_fetchRouteHandler != null)
        {
            return await _fetchRouteHandler(path);
        }
        return "{\"error\": \"No fetch handler registered\"}";
    }

    // ─── SignalR Hub Exports ──────────────────────────────────

    public static void RegisterHubInvokeHandler(Func<string, string, string, string?, Task<string?>> handler)
        => _hubInvokeHandler = handler;

    public static void RegisterHubConnectHandler(Func<string, Task<string>> handler)
        => _hubConnectHandler = handler;

    public static void RegisterHubDisconnectHandler(Func<string, string, Task> handler)
        => _hubDisconnectHandler = handler;

    /// <summary>
    /// Connect to a hub, returns connectionId
    /// </summary>
    [JSExport]
    public static async Task<string> HubConnect(string hubName)
    {
        if (_hubConnectHandler != null)
            return await _hubConnectHandler(hubName);
        return "";
    }

    /// <summary>
    /// Disconnect from a hub
    /// </summary>
    [JSExport]
    public static async Task HubDisconnect(string hubName, string connectionId)
    {
        if (_hubDisconnectHandler != null)
            await _hubDisconnectHandler(hubName, connectionId);
    }

    /// <summary>
    /// Invoke a method on a hub
    /// </summary>
    [JSExport]
    public static async Task<string?> HubInvoke(string hubName, string method, string connectionId, string? argsJson)
    {
        if (_hubInvokeHandler != null)
            return await _hubInvokeHandler(hubName, method, connectionId, argsJson);
        return "{\"error\": \"No hub handler registered\"}";
    }
}
