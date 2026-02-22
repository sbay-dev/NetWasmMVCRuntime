using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.Cepha.Http;
using WasmMvcRuntime.Cepha.SSE;
using WasmMvcRuntime.Core;

namespace WasmMvcRuntime.Cepha;

/// <summary>
/// The Cepha server orchestrator.
/// Wires together the MVC engine, SignalR engine, SSE connection manager,
/// and the HTTP request pipeline, then exposes them through <see cref="CephaExports"/>.
/// 
/// Named after Physarum polycephalum — a decentralized, adaptive organism
/// that efficiently routes information without a central brain.
/// </summary>
public class CephaServer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMvcEngine _mvcEngine;
    private readonly ISignalREngine _signalREngine;
    private readonly SseConnectionManager _sseManager;
    private readonly SseMiddleware _sseMiddleware;
    private readonly CephaMiddlewareDelegate _pipeline;
    private readonly DateTime _startedAt;

    public CephaServer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _mvcEngine = serviceProvider.GetRequiredService<IMvcEngine>();
        _signalREngine = serviceProvider.GetRequiredService<ISignalREngine>();
        _sseManager = serviceProvider.GetRequiredService<SseConnectionManager>();
        _sseMiddleware = serviceProvider.GetRequiredService<SseMiddleware>();
        _startedAt = DateTime.UtcNow;

        // ??? Build the request pipeline ??????????????????????
        var pipelineBuilder = new CephaRequestPipeline(_mvcEngine, serviceProvider);
        pipelineBuilder
            .UseExceptionHandler()
            .UseLogging()
            .UseCors()
            .UseHealthCheck()
            .UseResponseHeaders()
            .UseServiceScope();

        _pipeline = pipelineBuilder.Build();
    }

    /// <summary>
    /// Registers all handler delegates with <see cref="CephaExports"/>
    /// so the JS host can drive the server.
    /// </summary>
    public void Start()
    {
        CephaInterop.ConsoleLog("?? Cepha server starting...");

        // ??? HTTP request handler ????????????????????????????
        CephaExports.RegisterRequestHandler(HandleRequestAsync);

        // ??? SSE handlers ????????????????????????????????????
        CephaExports.RegisterSseConnectHandler(_sseMiddleware.OnConnectAsync);
        CephaExports.RegisterSseDisconnectHandler(_sseMiddleware.OnDisconnectAsync);

        // ??? SignalR handlers ????????????????????????????????
        CephaExports.RegisterHubConnectHandler(async (hubName) =>
        {
            return await _signalREngine.ConnectAsync(hubName);
        });

        CephaExports.RegisterHubDisconnectHandler(async (hubName, connectionId) =>
        {
            await _signalREngine.DisconnectAsync(hubName, connectionId);
        });

        CephaExports.RegisterHubInvokeHandler(async (hubName, method, connectionId, argsJson) =>
        {
            return await _signalREngine.InvokeAsync(hubName, method, connectionId, argsJson);
        });

        // ??? Server info handler ?????????????????????????????
        CephaExports.RegisterServerInfoHandler(GetServerInfoAsync);

        // ??? SSE connection events ???????????????????????????
        _sseManager.OnConnected += conn =>
            CephaInterop.ConsoleLog($"[Cepha] SSE+ {conn.ConnectionId} ? {conn.Path}");

        _sseManager.OnDisconnected += connId =>
            CephaInterop.ConsoleLog($"[Cepha] SSE- {connId}");

        // ??? Log startup info ????????????????????????????????
        var routes = (_mvcEngine as MvcEngine)?.GetRoutes();
        var hubNames = _signalREngine.GetHubNames();

        CephaInterop.ConsoleLog($"? Cepha ready — {routes?.Count ?? 0} routes, {hubNames.Count} hubs ({string.Join(", ", hubNames)})");
        CephaInterop.ConsoleLog("?? Waiting for requests from JS host...");
    }

    // ?????????????????????????????????????????????????????????
    // Request processing
    // ?????????????????????????????????????????????????????????

    private async Task<string> HandleRequestAsync(
        string requestId, string method, string path, string? headersJson, string? bodyContent)
    {
        var context = CephaHttpContext.FromRequest(
            requestId, method, path, headersJson, bodyContent, _serviceProvider);

        await _pipeline(context);

        return context.ToResponseJson();
    }

    // ?????????????????????????????????????????????????????????
    // Server info
    // ?????????????????????????????????????????????????????????

    private Task<string> GetServerInfoAsync()
    {
        var routes = (_mvcEngine as MvcEngine)?.GetRoutes();
        var hubNames = _signalREngine.GetHubNames();

        var info = new
        {
            server = "Cepha",
            version = "1.0.0",
            nameOrigin = "Physarum polycephalum",
            runtime = "WebAssembly (.NET 10)",
            status = "running",
            startedAt = _startedAt,
            uptime = (DateTime.UtcNow - _startedAt).TotalSeconds,
            routes = routes?.Keys.OrderBy(r => r).ToList() ?? new List<string>(),
            routeCount = routes?.Count ?? 0,
            hubs = hubNames.ToList(),
            hubCount = hubNames.Count,
            sse = new
            {
                activeConnections = _sseManager.ConnectionCount,
                channels = _sseManager.GetChannels().ToList()
            },
            capabilities = new[]
            {
                "mvc-controllers",
                "api-controllers",
                "razor-view-rendering",
                "signalr-hubs",
                "server-sent-events",
                "ef-core-sqlite",
                "aspnet-identity",
                "cors",
                "middleware-pipeline"
            }
        };

        return Task.FromResult(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ?????????????????????????????????????????????????????????
    // Public API for programmatic access
    // ?????????????????????????????????????????????????????????

    /// <summary>Gets the SSE connection manager for pushing events from services.</summary>
    public SseConnectionManager Sse => _sseManager;

    /// <summary>Gets the SignalR engine for programmatic hub operations.</summary>
    public ISignalREngine SignalR => _signalREngine;

    /// <summary>Gets the MVC engine for programmatic route invocation.</summary>
    public IMvcEngine Mvc => _mvcEngine;
}
