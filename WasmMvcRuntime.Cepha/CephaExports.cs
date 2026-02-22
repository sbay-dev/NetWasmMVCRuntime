using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace WasmMvcRuntime.Cepha;

/// <summary>
/// Methods exported to JavaScript via [JSExport].
/// The Node.js / Cloudflare Worker host calls these to drive the .NET server logic.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class CephaExports
{
    // ??? Handler delegates (wired in Program.cs) ?????????????

    private static Func<string, string, string, string?, string?, Task<string>>? _handleRequest;
    private static Func<string, string, Task>? _handleSseConnect;
    private static Func<string, Task>? _handleSseDisconnect;
    private static Func<string, Task<string>>? _hubConnect;
    private static Func<string, string, Task>? _hubDisconnect;
    private static Func<string, string, string, string?, Task<string?>>? _hubInvoke;
    private static Func<Task<string>>? _getServerInfo;

    // ??? Registration API ????????????????????????????????????

    public static void RegisterRequestHandler(Func<string, string, string, string?, string?, Task<string>> handler)
        => _handleRequest = handler;

    public static void RegisterSseConnectHandler(Func<string, string, Task> handler)
        => _handleSseConnect = handler;

    public static void RegisterSseDisconnectHandler(Func<string, Task> handler)
        => _handleSseDisconnect = handler;

    public static void RegisterHubConnectHandler(Func<string, Task<string>> handler)
        => _hubConnect = handler;

    public static void RegisterHubDisconnectHandler(Func<string, string, Task> handler)
        => _hubDisconnect = handler;

    public static void RegisterHubInvokeHandler(Func<string, string, string, string?, Task<string?>> handler)
        => _hubInvoke = handler;

    public static void RegisterServerInfoHandler(Func<Task<string>> handler)
        => _getServerInfo = handler;

    // ?????????????????????????????????????????????????????????
    // HTTP Request Pipeline
    // ?????????????????????????????????????????????????????????

    /// <summary>
    /// Main entry point called by JS for every incoming HTTP request.
    /// Returns a JSON envelope: { statusCode, contentType, body, headers }
    /// </summary>
    /// <param name="requestId">Unique request identifier from JS</param>
    /// <param name="method">HTTP method (GET, POST, PUT, DELETE, …)</param>
    /// <param name="path">Request path (e.g. /api/weather)</param>
    /// <param name="headersJson">Request headers as JSON object (nullable)</param>
    /// <param name="bodyContent">Request body content (nullable, for POST/PUT)</param>
    [JSExport]
    public static async Task<string> HandleRequest(
        string requestId, string method, string path, string? headersJson, string? bodyContent)
    {
        if (_handleRequest != null)
            return await _handleRequest(requestId, method, path, headersJson, bodyContent);

        return """{"statusCode":503,"contentType":"text/plain","body":"Cepha server not initialized"}""";
    }

    // ?????????????????????????????????????????????????????????
    // SSE (Server-Sent Events)
    // ?????????????????????????????????????????????????????????

    /// <summary>
    /// Called when a browser opens an SSE connection.
    /// The path determines which controller action streams events.
    /// </summary>
    [JSExport]
    public static async Task SseConnect(string connectionId, string path)
    {
        if (_handleSseConnect != null)
            await _handleSseConnect(connectionId, path);
    }

    /// <summary>
    /// Called when an SSE client disconnects.
    /// </summary>
    [JSExport]
    public static async Task SseDisconnect(string connectionId)
    {
        if (_handleSseDisconnect != null)
            await _handleSseDisconnect(connectionId);
    }

    // ?????????????????????????????????????????????????????????
    // SignalR Hub Operations
    // ?????????????????????????????????????????????????????????

    /// <summary>
    /// Connects a client to a SignalR hub, returns connectionId.
    /// </summary>
    [JSExport]
    public static async Task<string> HubConnect(string hubName)
    {
        if (_hubConnect != null)
            return await _hubConnect(hubName);
        return "";
    }

    /// <summary>
    /// Disconnects a client from a SignalR hub.
    /// </summary>
    [JSExport]
    public static async Task HubDisconnect(string hubName, string connectionId)
    {
        if (_hubDisconnect != null)
            await _hubDisconnect(hubName, connectionId);
    }

    /// <summary>
    /// Invokes a method on a SignalR hub.
    /// </summary>
    [JSExport]
    public static async Task<string?> HubInvoke(string hubName, string method, string connectionId, string? argsJson)
    {
        if (_hubInvoke != null)
            return await _hubInvoke(hubName, method, connectionId, argsJson);
        return """{"error":"No hub handler registered"}""";
    }

    // ?????????????????????????????????????????????????????????
    // Server Info / Health
    // ?????????????????????????????????????????????????????????

    /// <summary>
    /// Returns JSON with server status, registered routes, hubs, and uptime.
    /// </summary>
    [JSExport]
    public static async Task<string> GetServerInfo()
    {
        if (_getServerInfo != null)
            return await _getServerInfo();
        return """{"status":"not initialized"}""";
    }
}
