using System;
using System.Threading;
using System.Threading.Tasks;

// ═══════════════════════════════════════════════════════════════════
// 🧬 Browser-wasm shims for Microsoft.Extensions.Hosting & Logging
//    Shadows the real types so the WASM bundle doesn't need the
//    original assemblies (which get stripped by the publish pipeline).
// ═══════════════════════════════════════════════════════════════════

namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public abstract class BackgroundService : IHostedService
    {
        private CancellationTokenSource? _stoppingCts;

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = ExecuteAsync(_stoppingCts.Token);
            return Task.CompletedTask;
        }

        public virtual Task StopAsync(CancellationToken cancellationToken)
        {
            _stoppingCts?.Cancel();
            return Task.CompletedTask;
        }

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

        public virtual void Dispose() => _stoppingCts?.Dispose();
    }

    public interface IHostApplicationLifetime
    {
        CancellationToken ApplicationStarted { get; }
        CancellationToken ApplicationStopping { get; }
        CancellationToken ApplicationStopped { get; }
        void StopApplication();
    }
}

namespace Microsoft.Extensions.Logging
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    public readonly struct EventId
    {
        public EventId(int id, string? name = null) { Id = id; Name = name; }
        public int Id { get; }
        public string? Name { get; }
        public static implicit operator EventId(int id) => new(id);
    }

    public interface ILogger
    {
        void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter);
        bool IsEnabled(LogLevel logLevel);
        IDisposable? BeginScope<TState>(TState state) where TState : notnull;
    }

    public interface ILogger<out TCategoryName> : ILogger { }

    public interface ILoggerFactory : IDisposable
    {
        ILogger CreateLogger(string categoryName);
        void AddProvider(ILoggerProvider provider);
    }

    public interface ILoggerProvider : IDisposable
    {
        ILogger CreateLogger(string categoryName);
    }

    public static class LoggerExtensions
    {
        private static void DoLog(ILogger logger, LogLevel level, string? message, Exception? ex, object?[] args)
        {
            if (logger == null || !logger.IsEnabled(level)) return;
            string formatted = message ?? "";
            if (args != null && args.Length > 0)
            {
                try
                {
                    foreach (var arg in args)
                    {
                        var idx = formatted.IndexOf('{');
                        if (idx < 0) break;
                        var end = formatted.IndexOf('}', idx);
                        if (end < 0) break;
                        formatted = string.Concat(formatted.AsSpan(0, idx), arg?.ToString() ?? "null", formatted.AsSpan(end + 1));
                    }
                }
                catch { /* template mismatch — use raw */ }
            }
            if (ex != null) formatted += $" | Exception: {ex.GetType().Name}: {ex.Message}";
            logger.Log(level, default, formatted, null, (s, _) => s?.ToString() ?? "");
        }

        public static void LogTrace(this ILogger logger, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Trace, message, null, args);
        public static void LogDebug(this ILogger logger, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Debug, message, null, args);
        public static void LogInformation(this ILogger logger, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Information, message, null, args);
        public static void LogWarning(this ILogger logger, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Warning, message, null, args);
        public static void LogError(this ILogger logger, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Error, message, null, args);
        public static void LogError(this ILogger logger, Exception? exception, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Error, message, exception, args);
        public static void LogCritical(this ILogger logger, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Critical, message, null, args);
        public static void LogCritical(this ILogger logger, Exception? exception, string? message, params object?[] args)
            => DoLog(logger, LogLevel.Critical, message, exception, args);
    }

    /// <summary>Browser console logger — outputs to browser DevTools via JS interop.</summary>
    internal sealed class BrowserConsoleLogger<T> : ILogger<T>
    {
        private static readonly string _category = typeof(T).Name;
        public static readonly BrowserConsoleLogger<T> Instance = new();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None) return;
            var msg = formatter != null ? formatter(state, exception) : state?.ToString() ?? "";
            var prefix = logLevel switch
            {
                LogLevel.Trace       => "🔍",
                LogLevel.Debug       => "🐛",
                LogLevel.Information => "ℹ️",
                LogLevel.Warning     => "⚠️",
                LogLevel.Error       => "❌",
                LogLevel.Critical    => "🔥",
                _                    => "📝"
            };
            var line = $"{prefix} [{_category}] {msg}";
            try
            {
                if (logLevel >= LogLevel.Error)
                    Cepha.JsInterop.ConsoleError(line);
                else if (logLevel == LogLevel.Warning)
                    Cepha.JsInterop.ConsoleWarn(line);
                else
                    Cepha.JsInterop.ConsoleLog(line);
            }
            catch { /* JS interop not yet available during early boot */ }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
