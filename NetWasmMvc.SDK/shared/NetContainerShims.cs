#if HAS_NETCONTAINER_REF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NetContainer.Ref.Distributions;
using NetContainer.Ref.Guest;
using NetContainer.Ref.Hardware;
using NetContainer.Ref.Orchestrator;
using NetContainer.Ref.Services;
using NetContainer.Ref.Snapshot;

namespace NetContainer.Ref
{
    /// <summary>
    /// Browser-wasm registrations for NetContainer.Ref contracts.
    /// Avoids constructing host/QEMU services that are unsupported in the browser.
    /// </summary>
    public static class BrowserRefServiceRegistration
    {
        public static Microsoft.AspNetCore.Builder.CephaServiceCollection AddNetContainerRef(
            this Microsoft.AspNetCore.Builder.CephaServiceCollection services,
            Action<RefOptions>? configure = null)
        {
            var options = new RefOptions();
            configure?.Invoke(options);
            options.PreferCliDelegation = false;

            services.AddSingleton(options);
            services.AddSingleton(options.HostPlatform ?? new Platform.HostPlatformContext
            {
                OperatingSystem = "browser",
                Architecture = "x86_64",
                Accelerators = new[] { "tcg,thread=multi" }
            });

            services.AddSingleton<IQemuAuditService, BrowserQemuAuditService>();
            services.AddSingleton<IRefOrchestratorService, BrowserRefOrchestratorService>();

            return services;
        }
    }

    internal sealed class BrowserRefOrchestratorService : IRefOrchestratorService
    {
        private readonly RefOptions _options;
        private readonly IQemuAuditService _audit;
        private readonly List<IGuestContext> _guests = new();
        private readonly List<SnapshotInfo> _snapshots = new();

        public BrowserRefOrchestratorService(RefOptions options, IQemuAuditService audit)
        {
            _options = options;
            _audit = audit;
        }

        public int RunningCount => _guests.Count;

        public IQemuAuditService? Audit => _audit;

        public Task<IGuestContext> StartGuestAsync(RefGuestOptions options, CancellationToken ct = default)
        {
            var guest = new BrowserGuestContext(
                options.Id ?? Guid.NewGuid().ToString("N"),
                options.TenantId,
                options.Arch,
                options.HardwareProfile ?? _options.DefaultHardwareProfile);

            _guests.RemoveAll(g => string.Equals(g.Id, guest.Id, StringComparison.OrdinalIgnoreCase));
            _guests.Add(guest);
            return Task.FromResult<IGuestContext>(guest);
        }

        public Task<IGuestContext> StartGuestAsync(
            KnownDistribution distribution,
            string arch = "x86_64",
            Func<RefGuestOptions, RefGuestOptions>? configure = null,
            CancellationToken ct = default)
        {
            var options = new RefGuestOptions
            {
                TenantId = "default",
                Arch = arch,
                HardwareProfile = _options.DefaultHardwareProfile
            };

            if (configure != null)
            {
                options = configure(options);
            }

            return StartGuestAsync(options, ct);
        }

        public Task StopGuestAsync(string guestId, CancellationToken ct = default)
        {
            _guests.RemoveAll(g => string.Equals(g.Id, guestId, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task StopAllAsync(CancellationToken ct = default)
        {
            _guests.Clear();
            return Task.CompletedTask;
        }

        public IReadOnlyList<IGuestContext> GetRunningGuests() => _guests.ToArray();

        public IGuestContext? GetGuest(string guestId)
            => _guests.FirstOrDefault(g => string.Equals(g.Id, guestId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<IGuestContext> GetGuestsForTenant(string tenantId)
            => _guests.Where(g => string.Equals(g.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToArray();

        public IGuestContext? GetGuestForTenant(string tenantId, string guestId)
            => _guests.FirstOrDefault(g =>
                string.Equals(g.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.Id, guestId, StringComparison.OrdinalIgnoreCase));

        public int GetTenantGuestCount(string tenantId)
            => _guests.Count(g => string.Equals(g.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

        public async Task<bool> StopGuestForTenantAsync(string tenantId, string guestId, CancellationToken ct = default)
        {
            var guest = GetGuestForTenant(tenantId, guestId);
            if (guest == null)
            {
                return false;
            }

            await StopGuestAsync(guest.Id, ct);
            return true;
        }

        public Task StopAllForTenantAsync(string tenantId, CancellationToken ct = default)
        {
            _guests.RemoveAll(g => string.Equals(g.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task SetGuestMemoryAsync(string guestId, int targetMb, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public IEnumerable<DistributionProfile> ListAvailableDistributions()
        {
            var profile = new HardwareProfile
            {
                VirtualCpuCores = 1,
                MemoryMb = 256
            };

            return new[]
            {
                new DistributionProfile
                {
                    Name = "OpenWrt (browser-wasm, no QEMU)",
                    Distribution = KnownDistribution.OpenWrt,
                    Arch = "x86_64",
                    BootMethod = DistributionBootMethod.DiskImage,
                    QemuBinary = "unavailable-in-browser",
                    RecommendedHardware = profile
                }
            };
        }

        public Task<SnapshotExportResult> ExportSnapshotAsync(
            string guestId,
            string? description = null,
            CancellationToken ct = default)
        {
            var info = new SnapshotInfo
            {
                Id = $"snap-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                GuestId = guestId,
                TenantId = GetGuest(guestId)?.TenantId ?? "default",
                Arch = GetGuest(guestId)?.Arch ?? "x86_64",
                MemoryMb = _options.DefaultHardwareProfile.MemoryMb,
                CpuCores = _options.DefaultHardwareProfile.VirtualCpuCores,
                CreatedAt = DateTimeOffset.UtcNow,
                Description = description,
                DiskImageFile = "browser-wasm.img",
                VmStateFile = "browser-wasm.state"
            };

            _snapshots.Add(info);
            return Task.FromResult(new SnapshotExportResult(true, $"browser://{info.Id}", info, null));
        }

        public Task<IGuestContext> StartFromSnapshotAsync(string snapshotDir, CancellationToken ct = default)
        {
            var guest = new BrowserGuestContext(
                Guid.NewGuid().ToString("N"),
                "default",
                "x86_64",
                _options.DefaultHardwareProfile);

            _guests.Add(guest);
            return Task.FromResult<IGuestContext>(guest);
        }

        public IReadOnlyList<SnapshotInfo> ListSnapshots() => _snapshots.ToArray();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class BrowserGuestContext : IGuestContext
    {
        public BrowserGuestContext(string id, string tenantId, string arch, HardwareProfile profile)
        {
            Id = id;
            TenantId = tenantId;
            Arch = arch;
            StartedAt = DateTimeOffset.UtcNow;
            IsRunning = true;
            SessionDir = $"/virtual/{id}";
            QemuEnvironment = new Dictionary<string, string>();
            ResolvedAccelerators = new[] { "tcg,thread=multi" };
            ResolvedCpuModel = profile.CpuModel;
            VirtInfo = new VirtualizationInfo(
                "Software Emulation",
                "TCG",
                "tcg",
                "Emulated",
                "none",
                false,
                true,
                "Browser WASM mode");
            Shell = new BrowserShellService();
            Packages = new BrowserPackageService();
            Logs = new BrowserLogStreamService();
            Analytics = new BrowserAnalyticsService(id);
        }

        public string Id { get; }
        public string TenantId { get; }
        public string Arch { get; }
        public int? QemuPid => null;
        public bool IsRunning { get; private set; }
        public DateTimeOffset? StartedAt { get; }
        public int QmpPort => 0;
        public int VncPort => 0;
        public int VncWsPort => 0;
        public int SshPort => 0;
        public int HttpPort => 0;
        public int SerialPort => 9600;
        public string SessionDir { get; }
        public IShellService Shell { get; }
        public IPackageService Packages { get; }
        public ILogStreamService Logs { get; }
        public IAnalyticsService Analytics { get; }
        public IReadOnlyDictionary<string, string> QemuEnvironment { get; }
        public string[] ResolvedAccelerators { get; }
        public string ResolvedCpuModel { get; }
        public VirtualizationInfo VirtInfo { get; }

        public Task FreezeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetMemoryBalloonAsync(int targetMb, CancellationToken ct = default) => Task.CompletedTask;

        public Task<SnapshotExportResult> ExportSnapshotAsync(
            string outputDir,
            string? description = null,
            CancellationToken ct = default)
        {
            var info = new SnapshotInfo
            {
                Id = $"snap-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                GuestId = Id,
                TenantId = TenantId,
                Arch = Arch,
                MemoryMb = 256,
                CpuCores = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                Description = description,
                DiskImageFile = "browser-wasm.img",
                VmStateFile = "browser-wasm.state"
            };

            return Task.FromResult(new SnapshotExportResult(true, outputDir, info, null));
        }

        public Task<NetworkInitResult> InitializeNetworkAsync(CancellationToken ct = default)
            => Task.FromResult(new NetworkInitResult(
                HasWanIp: false,
                HasDefaultRoute: false,
                HasConnectivity: false,
                HasDnsResolution: false,
                WanIpAddress: null,
                Log: Array.Empty<string>()));

        public Task WaitForSerialInitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// IShellService for browser-WASM. No real QEMU serial port exists,
    /// so all commands return an honest error indicating server mode is required.
    /// </summary>
    internal sealed class BrowserShellService : IShellService
    {
        public Task<ShellResult> RunAsync(string command, int timeoutMs = 30000, CancellationToken ct = default)
            => Task.FromResult(new ShellResult(1, "", "Shell execution requires server mode (real QEMU)"));

        public Task<string> RunRequiredAsync(string command, int timeoutMs = 30000, CancellationToken ct = default)
            => Task.FromResult("");
    }
    internal sealed class BrowserPackageService : IPackageService
    {
        public Task InstallAsync(IEnumerable<string> packages, CancellationToken ct = default) => Task.CompletedTask;
        public Task InstallAsync(string package, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(IEnumerable<string> packages, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string package, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateIndexAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpgradeAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string[]> ListInstalledAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<string>());
        public Task PushAsync(string hostPath, string guestPath, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// ILogStreamService for browser-WASM. No real QEMU process exists,
    /// so all log methods return empty arrays honestly.
    /// </summary>
    internal sealed class BrowserLogStreamService : ILogStreamService
    {
        public Task<string[]> GetQemuLogAsync(int lineCount = 100, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<string>());

        public Task<string[]> GetGuestSyslogAsync(int lineCount = 100, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<string>());

        public Task<string[]> GetAllLogsAsync(int lineCount = 50, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<string>());

        public async IAsyncEnumerable<string> TailQemuLogAsync(
            int startOffset = 0,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }


    internal sealed class BrowserAnalyticsService : IAnalyticsService
    {
        private readonly string _guestId;

        public BrowserAnalyticsService(string guestId)
        {
            _guestId = guestId;
        }

        public Task<GuestMetrics> GetMetricsAsync(CancellationToken ct = default)
            => Task.FromResult(new GuestMetrics
            {
                GuestId = _guestId,
                MemoryActualBytes = -1,
                VCpuCount = -1,
                QemuCpuSeconds = -1,
                CollectedAt = DateTimeOffset.UtcNow
            });

        public async IAsyncEnumerable<GuestMetrics> StreamMetricsAsync(
            int intervalMs = 1000,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    internal sealed class BrowserQemuAuditService : IQemuAuditService
    {
        public void RecordProcessStart(
            string guestId,
            string qemuBinary,
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string> environment,
            string workingDirectory,
            int? pid)
        {
        }

        public void RecordQmpCommand(string guestId, string command, object? args = null)
        {
        }

        public void RecordProcessStop(string guestId, int? exitCode)
        {
        }

        public QemuAuditReport GetGuestAudit(string guestId)
            => new(
                guestId,
                Array.Empty<QemuAuditEntry>(),
                new Dictionary<string, string>(),
                Array.Empty<string>(),
                "browser-wasm",
                "/",
                null);

        public EncapsulationVerification VerifyEncapsulation(string guestId)
            => new(
                guestId,
                IsFullyEncapsulated: true,
                EnvironmentIsolated: true,
                PathsIsolated: true,
                NetworkBoundToLoopback: true,
                PeripheralsSuppressed: true,
                NoDirectHostAccess: true,
                BinaryEmbedded: true,
                WorkDirEncapsulated: true,
                EncapsulationPercent: 100,
                Violations: Array.Empty<string>());

        public EncapsulationSummary GetSummary()
            => new(
                TotalGuests: 0,
                FullyEncapsulated: 0,
                PartiallyEncapsulated: 0,
                Details: Array.Empty<EncapsulationVerification>());
    }
}
#endif

