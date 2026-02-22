using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.Core;

namespace WasmMvcRuntime.Cepha.Http;

/// <summary>
/// A middleware delegate for the Cepha request pipeline.
/// </summary>
public delegate Task CephaMiddlewareDelegate(CephaHttpContext context);

/// <summary>
/// Full HTTP request pipeline for the Cepha server.
/// Processes requests through a middleware chain: logging ? CORS ? auth ? routing ? MVC.
/// </summary>
public class CephaRequestPipeline
{
    private readonly IMvcEngine _mvcEngine;
    private readonly IServiceProvider _serviceProvider;
    private readonly List<Func<CephaMiddlewareDelegate, CephaMiddlewareDelegate>> _middlewares = new();

    public CephaRequestPipeline(IMvcEngine mvcEngine, IServiceProvider serviceProvider)
    {
        _mvcEngine = mvcEngine;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Adds a middleware to the pipeline.
    /// Middleware is executed in the order it is added.
    /// </summary>
    public CephaRequestPipeline Use(Func<CephaMiddlewareDelegate, CephaMiddlewareDelegate> middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Builds the pipeline and returns the composed delegate.
    /// </summary>
    public CephaMiddlewareDelegate Build()
    {
        CephaMiddlewareDelegate terminal = MvcTerminal;

        // Build in reverse order so the first Use() runs first
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            terminal = _middlewares[i](terminal);
        }

        return terminal;
    }

    /// <summary>
    /// Terminal middleware: routes the request through the MVC engine.
    /// </summary>
    private async Task MvcTerminal(CephaHttpContext context)
    {
        await _mvcEngine.ProcessRequestAsync(context);
    }

    // ?????????????????????????????????????????????????????????
    // Built-in middleware factories
    // ?????????????????????????????????????????????????????????

    /// <summary>
    /// Adds request logging middleware.
    /// </summary>
    public CephaRequestPipeline UseLogging()
    {
        return Use(next => async context =>
        {
            var sw = Stopwatch.StartNew();
            CephaInterop.ConsoleLog($"[Cepha] ? {context.Method} {context.Path}");

            await next(context);

            sw.Stop();
            CephaInterop.ConsoleLog($"[Cepha] ? {context.StatusCode} {context.ContentType} ({sw.ElapsedMilliseconds}ms)");
        });
    }

    /// <summary>
    /// Adds CORS preflight handling middleware.
    /// </summary>
    public CephaRequestPipeline UseCors(string allowedOrigins = "*")
    {
        return Use(next => async context =>
        {
            context.ResponseHeaders["Access-Control-Allow-Origin"] = allowedOrigins;
            context.ResponseHeaders["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, PATCH, OPTIONS";
            context.ResponseHeaders["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Requested-With";
            context.ResponseHeaders["Access-Control-Max-Age"] = "86400";

            // Handle preflight
            if (context.Method == "OPTIONS")
            {
                context.StatusCode = 204;
                context.ResponseBody = "";
                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// Adds exception handling middleware that wraps errors in a JSON response.
    /// </summary>
    public CephaRequestPipeline UseExceptionHandler()
    {
        return Use(next => async context =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                CephaInterop.ConsoleError($"[Cepha] Unhandled error: {ex.Message}");
                context.StatusCode = 500;
                context.ContentType = "application/json";
                context.ResponseBody = JsonSerializer.Serialize(new
                {
                    error = "Internal Server Error",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        });
    }

    /// <summary>
    /// Adds static-file / health-check middleware.
    /// Intercepts well-known paths before they hit the MVC pipeline.
    /// </summary>
    public CephaRequestPipeline UseHealthCheck(string path = "/health")
    {
        return Use(next => async context =>
        {
            if (context.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                context.StatusCode = 200;
                context.ContentType = "application/json";
                context.ResponseBody = JsonSerializer.Serialize(new
                {
                    status = "healthy",
                    server = "Cepha/1.0",
                    runtime = "WebAssembly",
                    timestamp = DateTime.UtcNow,
                    uptime = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds
                });
                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// Adds a middleware that injects a scoped DI container into RequestServices.
    /// </summary>
    public CephaRequestPipeline UseServiceScope()
    {
        return Use(next => async context =>
        {
            using var scope = _serviceProvider.CreateScope();
            context.RequestServices = scope.ServiceProvider;
            await next(context);
        });
    }

    /// <summary>
    /// Adds response compression hint header.
    /// Actual compression is handled by the JS host / reverse proxy.
    /// </summary>
    public CephaRequestPipeline UseResponseHeaders()
    {
        return Use(next => async context =>
        {
            await next(context);

            // Cache control for API responses
            if (context.ContentType.Contains("application/json"))
            {
                context.ResponseHeaders.TryAdd("Cache-Control", "no-store");
            }

            // Content security
            context.ResponseHeaders.TryAdd("X-Content-Type-Options", "nosniff");
            context.ResponseHeaders.TryAdd("X-Frame-Options", "DENY");
        });
    }
}
