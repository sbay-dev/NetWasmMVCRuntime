using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

// ═══════════════════════════════════════════════════════════════════
// 🧬 Browser-wasm logger — outputs to browser DevTools via JS interop.
//    All hosting/logging interfaces and base classes (IHostedService,
//    BackgroundService, ILogger<T>, LogLevel, LoggerExtensions, etc.)
//    come from the REAL Microsoft.Extensions packages:
//      • Microsoft.Extensions.Hosting.Abstractions
//      • Microsoft.Extensions.Logging.Abstractions
//      • Microsoft.Extensions.Logging
//    These are auto-referenced by the SDK — no shadowing needed.
//    This file provides ONLY the WASM-specific ILogger implementation.
// ═══════════════════════════════════════════════════════════════════

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// Browser console logger — outputs to browser DevTools via JS interop.
    /// Implements the REAL <see cref="ILogger{T}"/> from Microsoft.Extensions.Logging.Abstractions.
    /// Registered as open-generic in CephaApp.Create via:
    /// <code>services.AddSingleton(typeof(ILogger&lt;&gt;), typeof(BrowserConsoleLogger&lt;&gt;));</code>
    /// </summary>
    internal sealed class BrowserConsoleLogger<T> : ILogger<T>
    {
        private static readonly string _category = typeof(T).Name;
        public static readonly BrowserConsoleLogger<T> Instance = new();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None) return;
            var msg = formatter != null ? formatter(state, exception) : state?.ToString() ?? "";
            if (exception != null && !msg.Contains(exception.Message))
                msg += $" | {exception.GetType().Name}: {exception.Message}";
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
