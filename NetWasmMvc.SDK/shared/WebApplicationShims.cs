using System;
using System.Threading;
using Cepha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetContainer.Ref;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Browser-wasm service collection that keeps Program.cs startup calls intact.
    /// </summary>
    public sealed class CephaServiceCollection : ServiceCollection
    {
        internal readonly List<Type> HostedServiceTypes = new();

        public CephaServiceCollection AddControllersWithViews() => this;

        public CephaServiceCollection AddHostedService<THostedService>()
            where THostedService : class, IHostedService
        {
            HostedServiceTypes.Add(typeof(THostedService));
            this.AddSingleton(typeof(THostedService));
            return this;
        }

        public CephaServiceCollection AddNetContainerRef(Action<RefOptions>? configure = null)
            => NetContainer.Ref.BrowserRefServiceRegistration.AddNetContainerRef(this, configure);
    }

    /// <summary>
    /// Minimal host environment surface used by Program.cs.
    /// </summary>
    public sealed class CephaHostEnvironment
    {
        public bool IsDevelopment() => true;
    }

    /// <summary>
    /// Browser-wasm WebApplicationBuilder shim.
    /// </summary>
    public sealed class WebApplicationBuilder
    {
        public CephaServiceCollection Services { get; } = new();

        public WebApplication Build() => new(Services);
    }

    /// <summary>
    /// Browser-wasm WebApplication shim that routes startup to CephaApp.
    /// </summary>
    public sealed class WebApplication
    {
        private readonly CephaServiceCollection _services;
        private CephaApplication? _app;
        private bool _started;

        internal WebApplication(CephaServiceCollection services)
        {
            _services = services;
        }

        public static WebApplicationBuilder CreateBuilder(string[]? args = null) => new();

        public CephaHostEnvironment Environment { get; } = new();

        public WebApplication UseExceptionHandler(string path) => this;
        public WebApplication UseHsts() => this;
        public WebApplication UseRouting() => this;
        public WebApplication UseAuthorization() => this;
        public WebApplication UseWebSockets() => this;

        public CephaEndpointConventionBuilder MapNetContainerTerminal(string pattern = "/nc-terminal/{guestId}")
            => new();

        public void Run()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _app = CephaApp.Create(services =>
            {
                foreach (var descriptor in _services)
                {
                    services.Add(descriptor);
                }
            });

            // Start registered hosted services (e.g., GuestBootService)
            foreach (var type in _services.HostedServiceTypes)
            {
                try
                {
                    var service = (Microsoft.Extensions.Hosting.IHostedService)_app.Services.GetRequiredService(type);
                    service.StartAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    try { Cepha.JsInterop.ConsoleError($"🧬 HostedService {type.Name} failed: {ex.Message}"); }
                    catch { }
                }
            }

            // Use async void wrapper to surface unobserved exceptions
            // instead of silently discarding faulted Tasks with _ = ...
            RunCephaAsync();
        }

        private async void RunCephaAsync()
        {
            try
            {
                await _app!.RunAsync("/");
            }
            catch (Exception ex)
            {
                try { Cepha.JsInterop.ConsoleError($"🧬 CephaApp.RunAsync failed: {ex}"); }
                catch { /* last resort — JS interop itself failed */ }
            }
        }
    }
}

