using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.Core;

namespace WasmMvcRuntime.Cepha.SSE;

/// <summary>
/// Processes SSE connections by routing them through the MVC pipeline.
/// When a controller action returns an <see cref="SseResult"/>, the middleware
/// streams events to the connected client via the <see cref="SseConnectionManager"/>.
/// </summary>
public class SseMiddleware
{
    private readonly IMvcEngine _mvcEngine;
    private readonly SseConnectionManager _sseManager;
    private readonly IServiceProvider _serviceProvider;

    public SseMiddleware(IMvcEngine mvcEngine, SseConnectionManager sseManager, IServiceProvider serviceProvider)
    {
        _mvcEngine = mvcEngine;
        _sseManager = sseManager;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Called when a new SSE connection is established.
    /// Routes the path through the MVC engine and, if the result is SSE-compatible,
    /// begins streaming events.
    /// </summary>
    public async Task OnConnectAsync(string connectionId, string path)
    {
        var connection = _sseManager.Connect(connectionId, path);
        CephaInterop.ConsoleLog($"[SSE] Client connected: {connectionId} ? {path}");

        try
        {
            // Process the initial request through MVC
            using var scope = _serviceProvider.CreateScope();
            var context = new InternalHttpContext
            {
                Path = path,
                Method = "GET",
                RequestServices = scope.ServiceProvider
            };

            await _mvcEngine.ProcessRequestAsync(context);

            // Send the initial response as an SSE data frame
            if (!string.IsNullOrEmpty(context.ResponseBody))
            {
                var payload = new SsePayload
                {
                    StatusCode = context.StatusCode,
                    ContentType = context.ContentType,
                    Body = context.ResponseBody
                };

                _sseManager.Send(connectionId, "data", payload);
            }

            // Send a "ready" event so the client knows the stream is active
            _sseManager.Send(connectionId, "ready", new { path, connectionId });
        }
        catch (Exception ex)
        {
            CephaInterop.ConsoleError($"[SSE] Error processing {path}: {ex.Message}");
            _sseManager.Send(connectionId, "error", new { error = ex.Message });
        }
    }

    /// <summary>
    /// Called when an SSE connection is closed by the client.
    /// </summary>
    public Task OnDisconnectAsync(string connectionId)
    {
        _sseManager.Disconnect(connectionId);
        CephaInterop.ConsoleLog($"[SSE] Client disconnected: {connectionId}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pushes a controller action result to all SSE clients subscribed to the given path.
    /// Call this from a controller or service to stream real-time updates.
    /// </summary>
    public async Task PushToChannelAsync(string path, string eventName = "update")
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = new InternalHttpContext
            {
                Path = path,
                Method = "GET",
                RequestServices = scope.ServiceProvider
            };

            await _mvcEngine.ProcessRequestAsync(context);

            if (!string.IsNullOrEmpty(context.ResponseBody))
            {
                var channel = path.Trim('/').ToLowerInvariant().Replace('/', '.');
                _sseManager.SendToChannel(channel, eventName, new SsePayload
                {
                    StatusCode = context.StatusCode,
                    ContentType = context.ContentType,
                    Body = context.ResponseBody
                });
            }
        }
        catch (Exception ex)
        {
            CephaInterop.ConsoleError($"[SSE] Push error for {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Broadcasts arbitrary data to all SSE clients.
    /// </summary>
    public void BroadcastEvent(string eventName, object data)
    {
        _sseManager.Broadcast(eventName, data);
    }
}

/// <summary>
/// Payload envelope for SSE data frames.
/// </summary>
public class SsePayload
{
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = "text/html";
    public string Body { get; set; } = string.Empty;
}
