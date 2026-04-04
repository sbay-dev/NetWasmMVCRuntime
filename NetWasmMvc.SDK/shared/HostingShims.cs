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
        public static void LogTrace(this ILogger logger, string? message, params object?[] args) { }
        public static void LogDebug(this ILogger logger, string? message, params object?[] args) { }
        public static void LogInformation(this ILogger logger, string? message, params object?[] args) { }
        public static void LogWarning(this ILogger logger, string? message, params object?[] args) { }
        public static void LogError(this ILogger logger, string? message, params object?[] args) { }
        public static void LogError(this ILogger logger, Exception? exception, string? message, params object?[] args) { }
        public static void LogCritical(this ILogger logger, string? message, params object?[] args) { }
        public static void LogCritical(this ILogger logger, Exception? exception, string? message, params object?[] args) { }
    }

    /// <summary>Browser-wasm no-op logger for DI resolution.</summary>
    internal sealed class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) { }
        public bool IsEnabled(LogLevel logLevel) => false;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
