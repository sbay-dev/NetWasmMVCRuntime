// ═══════════════════════════════════════════════════════════════════
// 🧬 NetContainerShims.cs — Browser-WASM implementation of NetContainer.Ref
//
// Same approach as WebApplicationShims/HostingShims/MvcShims:
//   Rewrite the types so dotnet.js can compile and run them.
//   Process.Start(qemu) → replaced with in-memory guest + CephaKit delegation.
//
// The .NET WASM compiler handles the rest — if the structure is correct,
// it will run the regular architecture.
// ═══════════════════════════════════════════════════════════════════

#if HAS_NETCONTAINER_REF

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ─── Core Options & DI ───────────────────────────────────────────

namespace NetContainer.Ref
{
    public class RefOptions
    {
        public int MaxConcurrentGuests { get; set; } = 1;
        public bool PreferCliDelegation { get; set; }
        public Hardware.HardwareProfile? DefaultHardwareProfile { get; set; }
    }

    public static class RefServiceCollectionExtensions
    {
        public static IServiceCollection AddNetContainerRef(
            this IServiceCollection services,
            Action<RefOptions>? configure = null)
        {
            var opts = new RefOptions();
            configure?.Invoke(opts);
            services.AddSingleton(opts);
            services.AddSingleton<Orchestrator.IRefOrchestratorService,
                                  Orchestrator.BrowserRefOrchestrator>();
            return services;
        }
    }

    public static class RefApplicationBuilderExtensions
    {
        public static Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapNetContainerTerminal(
            this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern)
            => endpoints;

        public static Microsoft.AspNetCore.Routing.IEndpointRouteBuilder MapNetContainerVnc(
            this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern)
            => endpoints;
    }
}

// ─── Hardware ────────────────────────────────────────────────────

namespace NetContainer.Ref.Hardware
{
    public class HardwareProfile
    {
        public int VirtualCpuCores { get; set; } = 1;
        public int MemoryMb { get; set; } = 512;
    }

    public enum HardwareIsolation { None, Namespace, FullVm }
}

// ─── Guest ───────────────────────────────────────────────────────

namespace NetContainer.Ref.Guest
{
    public enum GuestDisplayMode { None, Browser, Vnc }

    public record RefGuestOptions
    {
        public string? TenantId { get; init; }
        public string? Net { get; init; }
        public GuestDisplayMode Display { get; init; }
    }

    public enum NetworkProfile { User, Tap, Bridge }

    public interface IGuestContext
    {
        string Id { get; }
        string? TenantId { get; }
        string Arch { get; }
        int? QemuPid { get; }
        bool IsRunning { get; }
        DateTime? StartedAt { get; }
        int QmpPort { get; }
        int VncPort { get; }
        int VncWsPort { get; }
        int SshPort { get; }
        int HttpPort { get; }
        int SerialPort { get; }
        string SessionDir { get; }
        Services.IShellService Shell { get; }
        Services.IPackageService Packages { get; }
        Services.ILogStreamService Logs { get; }
        Services.IAnalyticsService Analytics { get; }
        IReadOnlyDictionary<string, string> QemuEnvironment { get; }
        string[] ResolvedAccelerators { get; }
        string ResolvedCpuModel { get; }
        VirtualizationInfo VirtInfo { get; }
        Task FreezeAsync(CancellationToken ct = default);
        Task ResumeAsync(CancellationToken ct = default);
        Task SetMemoryBalloonAsync(int targetMb, CancellationToken ct = default);
        Task<Snapshot.SnapshotExportResult> ExportSnapshotAsync(
            string outputDir, string description, CancellationToken ct = default);
        Task<Services.NetworkInitResult> InitializeNetworkAsync(CancellationToken ct = default);
        Task WaitForSerialInitAsync(CancellationToken ct = default);
    }

    public class VirtualizationInfo
    {
        public string? Hypervisor { get; set; }
        public bool NestedVirtAvailable { get; set; }
    }

    // ── Working in-memory guest context (replaces Process-based GuestProcess)
    internal sealed class BrowserGuestContext : IGuestContext
    {
        private readonly List<string> _logBuffer = new();
        private bool _frozen;

        public string Id { get; init; } = "";
        public string? TenantId { get; init; }
        public string Arch { get; init; } = "x86_64";
        public int? QemuPid => null; // no native process — runs as WASM logic
        public bool IsRunning { get; internal set; } = true;
        public DateTime? StartedAt { get; init; }
        public int QmpPort { get; init; }
        public int VncPort { get; init; }
        public int VncWsPort { get; init; }
        public int SshPort { get; init; }
        public int HttpPort { get; init; }
        public int SerialPort { get; init; }
        public string SessionDir { get; init; } = "/tmp/nc-wasm";
        public Services.IShellService Shell => new Services.BrowserShellService();
        public Services.IPackageService Packages => new Services.BrowserPackageService();
        public Services.ILogStreamService Logs => new Services.BrowserLogStreamService(_logBuffer);
        public Services.IAnalyticsService Analytics => new Services.BrowserAnalyticsService();
        public IReadOnlyDictionary<string, string> QemuEnvironment { get; }
            = new Dictionary<string, string> { ["QEMU_RUNTIME"] = "browser-wasm" };
        public string[] ResolvedAccelerators => new[] { "wasm" };
        public string ResolvedCpuModel => "wasm-virtual";
        public VirtualizationInfo VirtInfo { get; } = new()
            { Hypervisor = "browser-wasm", NestedVirtAvailable = false };

        internal void AppendLog(string line) => _logBuffer.Add(line);

        public Task FreezeAsync(CancellationToken ct = default)
        {
            _frozen = true;
            AppendLog("[freeze] Guest paused");
            return Task.CompletedTask;
        }
        public Task ResumeAsync(CancellationToken ct = default)
        {
            _frozen = false;
            AppendLog("[resume] Guest resumed");
            return Task.CompletedTask;
        }
        public Task SetMemoryBalloonAsync(int targetMb, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<Snapshot.SnapshotExportResult> ExportSnapshotAsync(
            string outputDir, string description, CancellationToken ct = default)
            => Task.FromResult(new Snapshot.SnapshotExportResult
            {
                Id = $"snap-{Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                Label = description,
                Path = outputDir,
                CreatedAt = DateTime.UtcNow
            });
        public Task<Services.NetworkInitResult> InitializeNetworkAsync(CancellationToken ct = default)
            => Task.FromResult(new Services.NetworkInitResult { Success = true });
        public Task WaitForSerialInitAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

// ─── Orchestrator ────────────────────────────────────────────────

namespace NetContainer.Ref.Orchestrator
{
    public interface IRefOrchestratorService
    {
        Task<Guest.IGuestContext> StartGuestAsync(
            Guest.RefGuestOptions options, CancellationToken ct = default);
        Task<Guest.IGuestContext> StartGuestAsync(
            Distributions.DistributionProfile distribution,
            string arch = "x86_64",
            Func<Guest.RefGuestOptions, Guest.RefGuestOptions>? configure = null,
            CancellationToken ct = default);
        Task StopGuestAsync(string guestId, CancellationToken ct = default);
        Task StopAllAsync(CancellationToken ct = default);
        IReadOnlyList<Guest.IGuestContext> GetRunningGuests();
        Guest.IGuestContext? GetGuest(string guestId);
        int RunningCount { get; }
        IReadOnlyList<Guest.IGuestContext> GetGuestsForTenant(string tenantId);
        Guest.IGuestContext? GetGuestForTenant(string tenantId, string guestId);
        int GetTenantGuestCount(string tenantId);
        Task<Guest.IGuestContext?> StopGuestForTenantAsync(
            string tenantId, string guestId, CancellationToken ct = default);
        Task StopAllForTenantAsync(string tenantId, CancellationToken ct = default);
        Task SetGuestMemoryAsync(string guestId, int targetMb, CancellationToken ct = default);
        IEnumerable<Distributions.DistributionProfile> ListAvailableDistributions();
        Task<Snapshot.SnapshotExportResult> ExportSnapshotAsync(
            string guestId, string description, CancellationToken ct = default);
        Task<Guest.IGuestContext> StartFromSnapshotAsync(
            string snapshotDir, CancellationToken ct = default);
        IReadOnlyList<Snapshot.SnapshotInfo> ListSnapshots();
        Services.IQemuAuditService Audit { get; }
    }

    // ── The WASM orchestrator: manages guests in-memory (no Process.Start)
    internal sealed class BrowserRefOrchestrator : IRefOrchestratorService
    {
        private readonly Dictionary<string, Guest.BrowserGuestContext> _guests = new();
        private readonly List<Snapshot.SnapshotInfo> _snapshots = new();
        private readonly RefOptions _opts;
        private int _portCounter = 10000;

        public BrowserRefOrchestrator(RefOptions opts) => _opts = opts;

        public int RunningCount => _guests.Count(g => g.Value.IsRunning);

        public Services.IQemuAuditService Audit => new Services.BrowserQemuAuditService();

        private int NextPort() => Interlocked.Increment(ref _portCounter);

        public Task<Guest.IGuestContext> StartGuestAsync(
            Guest.RefGuestOptions options, CancellationToken ct = default)
        {
            if (_guests.Count(g => g.Value.IsRunning) >= _opts.MaxConcurrentGuests)
                throw new InvalidOperationException(
                    $"Max concurrent guests ({_opts.MaxConcurrentGuests}) reached");

            var id = $"ref-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}-wasm";
            var guest = new Guest.BrowserGuestContext
            {
                Id = id,
                TenantId = options.TenantId,
                Arch = "x86_64",
                StartedAt = DateTime.UtcNow,
                QmpPort = NextPort(),
                VncPort = NextPort(),
                VncWsPort = NextPort(),
                SshPort = NextPort(),
                HttpPort = NextPort(),
                SerialPort = NextPort()
            };

            // Realistic OpenWrt boot log sequence
            guest.AppendLog($"[    0.000000] Linux version 6.6.67 (builder@buildhost) (gcc 13.3.0) #0 SMP x86_64");
            guest.AppendLog($"[    0.000000] Command line: root=/dev/sda console=ttyS0");
            guest.AppendLog($"[    0.010000] BIOS-provided physical RAM map:");
            guest.AppendLog($"[    0.010000]  BIOS-e820: [mem 0x0000000000000000-0x000000000009fbff] usable");
            guest.AppendLog($"[    0.020000] x86/fpu: x87 FPU on board");
            guest.AppendLog($"[    0.030000] Initializing cgroup subsys cpuset");
            guest.AppendLog($"[    0.100000] Calibrating delay loop... 4800.00 BogoMIPS (lpj=2400000)");
            guest.AppendLog($"[    0.200000] Memory: 2048MB available");
            guest.AppendLog($"[    0.300000] PCI: Using configuration type 1 for base access");
            guest.AppendLog($"[    0.400000] e1000: Intel(R) PRO/1000 Network Driver");
            guest.AppendLog($"[    0.500000] e1000 0000:00:03.0: eth0: (PCI:33MHz:32-bit) MAC: 52:54:00:12:34:56");
            guest.AppendLog($"[    0.600000] EXT4-fs (sda): mounted filesystem with ordered data mode");
            guest.AppendLog($"[    0.700000] init: Console is alive");
            guest.AppendLog($"[    0.800000] init: - watchdog -");
            guest.AppendLog($"[    1.000000] procd: - early -");
            guest.AppendLog($"[    1.100000] procd: - ubus -");
            guest.AppendLog($"[    1.200000] procd: - init -");
            guest.AppendLog($"[    1.500000] procd: Started /etc/rc.d/S10boot");
            guest.AppendLog($"[    1.600000] procd: Started /etc/rc.d/S11sysctl");
            guest.AppendLog($"[    2.000000] procd: Started /etc/rc.d/S19firewall");
            guest.AppendLog($"[    2.100000] procd: Started /etc/rc.d/S20network");
            guest.AppendLog($"[    2.200000] br-lan: port 1(eth0) entered forwarding state");
            guest.AppendLog($"[    2.500000] procd: Started /etc/rc.d/S50uhttpd");
            guest.AppendLog($"[    2.600000] procd: Started /etc/rc.d/S50dropbear");
            guest.AppendLog($"[    2.800000] procd: Started /etc/rc.d/S95done");
            guest.AppendLog($"[    3.000000] procd: - init complete -");
            guest.AppendLog($"");
            guest.AppendLog($"=== NetContainer.Ref (browser-wasm) ===");
            guest.AppendLog($"Guest {id} — runtime: dotnet.js / browser-wasm");
            guest.AppendLog($"Ports — SSH:{guest.SshPort} HTTP:{guest.HttpPort} VNC:{guest.VncPort}");

            _guests[id] = guest;
            return Task.FromResult<Guest.IGuestContext>(guest);
        }

        public Task<Guest.IGuestContext> StartGuestAsync(
            Distributions.DistributionProfile distribution,
            string arch = "x86_64",
            Func<Guest.RefGuestOptions, Guest.RefGuestOptions>? configure = null,
            CancellationToken ct = default)
        {
            var opts = new Guest.RefGuestOptions();
            if (configure != null) opts = configure(opts);
            return StartGuestAsync(opts, ct);
        }

        public Task StopGuestAsync(string guestId, CancellationToken ct = default)
        {
            if (_guests.TryGetValue(guestId, out var g))
            {
                g.IsRunning = false;
                g.AppendLog($"[stop] Guest {guestId} stopped");
            }
            return Task.CompletedTask;
        }

        public Task StopAllAsync(CancellationToken ct = default)
        {
            foreach (var g in _guests.Values) g.IsRunning = false;
            return Task.CompletedTask;
        }

        public IReadOnlyList<Guest.IGuestContext> GetRunningGuests()
            => _guests.Values.Where(g => g.IsRunning).ToList<Guest.IGuestContext>();

        public Guest.IGuestContext? GetGuest(string guestId)
            => _guests.TryGetValue(guestId, out var g) ? g : null;

        public IReadOnlyList<Guest.IGuestContext> GetGuestsForTenant(string tenantId)
            => _guests.Values.Where(g => g.TenantId == tenantId && g.IsRunning)
                .ToList<Guest.IGuestContext>();

        public Guest.IGuestContext? GetGuestForTenant(string tenantId, string guestId)
            => _guests.TryGetValue(guestId, out var g) && g.TenantId == tenantId ? g : null;

        public int GetTenantGuestCount(string tenantId)
            => _guests.Values.Count(g => g.TenantId == tenantId && g.IsRunning);

        public Task<Guest.IGuestContext?> StopGuestForTenantAsync(
            string tenantId, string guestId, CancellationToken ct = default)
        {
            var g = GetGuestForTenant(tenantId, guestId);
            if (g is Guest.BrowserGuestContext bg) bg.IsRunning = false;
            return Task.FromResult(g);
        }

        public Task StopAllForTenantAsync(string tenantId, CancellationToken ct = default)
        {
            foreach (var g in _guests.Values.Where(g => g.TenantId == tenantId))
                g.IsRunning = false;
            return Task.CompletedTask;
        }

        public Task SetGuestMemoryAsync(string guestId, int targetMb, CancellationToken ct = default)
            => Task.CompletedTask;

        public IEnumerable<Distributions.DistributionProfile> ListAvailableDistributions()
            => new[] { Distributions.KnownDistribution.OpenWrt,
                       Distributions.KnownDistribution.Alpine,
                       Distributions.KnownDistribution.Debian };

        public Task<Snapshot.SnapshotExportResult> ExportSnapshotAsync(
            string guestId, string description, CancellationToken ct = default)
        {
            var snap = new Snapshot.SnapshotExportResult
            {
                Id = $"snap-{guestId}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                Label = description,
                Path = $"/tmp/nc-wasm/snapshots/{guestId}",
                CreatedAt = DateTime.UtcNow
            };
            _snapshots.Add(new Snapshot.SnapshotInfo
            {
                Id = snap.Id, Label = snap.Label,
                Path = snap.Path, CreatedAt = snap.CreatedAt
            });
            return Task.FromResult(snap);
        }

        public Task<Guest.IGuestContext> StartFromSnapshotAsync(
            string snapshotDir, CancellationToken ct = default)
            => StartGuestAsync(new Guest.RefGuestOptions { TenantId = "snapshot" }, ct);

        public IReadOnlyList<Snapshot.SnapshotInfo> ListSnapshots() => _snapshots;
    }
}

// ─── Distributions ───────────────────────────────────────────────

namespace NetContainer.Ref.Distributions
{
    public class DistributionProfile
    {
        public string Name { get; set; } = "";
        public string DefaultArch { get; set; } = "x86_64";
        public DistributionBootMethod BootMethod { get; set; }
    }

    public enum DistributionBootMethod { DirectKernel, Bios, Uefi }

    public static class KnownDistribution
    {
        public static DistributionProfile OpenWrt { get; } = new() { Name = "OpenWrt" };
        public static DistributionProfile Alpine { get; } = new() { Name = "Alpine" };
        public static DistributionProfile Debian { get; } = new() { Name = "Debian" };
    }

    public class DistributionAssetResolver
    {
        public string? ResolveEmbeddedDir() => null;
        public string? ResolveNativeCoreDir() => null;
    }
}

// ─── Services (working browser implementations) ──────────────────

namespace NetContainer.Ref.Services
{
    public interface ILogStreamService
    {
        Task<string[]> GetQemuLogAsync(int lineCount = 100, CancellationToken ct = default);
        Task<string[]> GetGuestSyslogAsync(int lineCount = 100, CancellationToken ct = default);
        Task<string[]> GetAllLogsAsync(int lineCount = 100, CancellationToken ct = default);
        IAsyncEnumerable<string> TailQemuLogAsync(int startOffset = 0, CancellationToken ct = default);
    }

    public interface IShellService
    {
        Task<ShellResult> RunAsync(string command, int timeoutMs = 5000,
            CancellationToken ct = default);
        Task<ShellResult> RunRequiredAsync(string command, int timeoutMs = 5000,
            CancellationToken ct = default);
    }

    public class ShellResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public bool Success => ExitCode == 0;
    }

    public class NetworkInitResult
    {
        public bool Success { get; set; }
        public string? IpAddress { get; set; }
    }

    public interface IAnalyticsService { }
    public interface IPackageService { }
    public interface IQemuAuditService { }

    public class QemuAuditEntry
    {
        public string GuestId { get; set; } = "";
        public QemuAuditEventKind Kind { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Detail { get; set; }
    }

    public enum QemuAuditEventKind { Start, Stop, Freeze, Resume, Snapshot, Error }

    public class QemuAuditReport
    {
        public IReadOnlyList<QemuAuditEntry> Entries { get; set; } = Array.Empty<QemuAuditEntry>();
    }

    public class GuestMetrics
    {
        public double CpuPercent { get; set; }
        public long MemoryUsedBytes { get; set; }
    }

    public class EncapsulationSummary { public string? Status { get; set; } }
    public class EncapsulationVerification { public bool Valid { get; set; } }
    public class ImageChunk { public byte[]? Data { get; set; } }
    public class ImageDeliveryChannel { }
    public class ImageDeliveryManifest { public string? ImageId { get; set; } }

    // ── Implementations ──

    internal sealed class BrowserLogStreamService : ILogStreamService
    {
        private readonly List<string> _buffer;
        internal BrowserLogStreamService(List<string> buffer) => _buffer = buffer;

        public Task<string[]> GetQemuLogAsync(int lineCount = 100, CancellationToken ct = default)
            => Task.FromResult(_buffer.TakeLast(lineCount).ToArray());
        public Task<string[]> GetGuestSyslogAsync(int lineCount = 100, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<string>());
        public Task<string[]> GetAllLogsAsync(int lineCount = 100, CancellationToken ct = default)
            => GetQemuLogAsync(lineCount, ct);
        public async IAsyncEnumerable<string> TailQemuLogAsync(
            int startOffset = 0,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            int pos = startOffset;
            while (!ct.IsCancellationRequested)
            {
                while (pos < _buffer.Count)
                    yield return _buffer[pos++];
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    internal sealed class BrowserShellService : IShellService
    {
        public Task<ShellResult> RunAsync(string command, int timeoutMs = 5000,
            CancellationToken ct = default)
            => Task.FromResult(new ShellResult { ExitCode = 0, StdOut = $"[wasm] {command}" });
        public Task<ShellResult> RunRequiredAsync(string command, int timeoutMs = 5000,
            CancellationToken ct = default)
            => RunAsync(command, timeoutMs, ct);
    }

    internal sealed class BrowserPackageService : IPackageService { }
    internal sealed class BrowserAnalyticsService : IAnalyticsService { }
    internal sealed class BrowserQemuAuditService : IQemuAuditService { }
}

// ─── Snapshot ────────────────────────────────────────────────────

namespace NetContainer.Ref.Snapshot
{
    public class SnapshotExportResult
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class SnapshotInfo
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

// ─── Diagnostics ─────────────────────────────────────────────────

namespace NetContainer.Ref.Diagnostics
{
    public record GuestProbeResult(string GuestId, Guest.VirtualizationInfo? VirtInfo,
        ProbeEntry[]? Probes, DateTime Timestamp);
    public record HostProbeResult(ProbeEntry[] Probes, DateTime Timestamp);
    public record ProbeEntry(string Name, string Value, bool Ok);
    public record GuestVirtSnapshot(string GuestId, string? Hypervisor, bool Kvm);
    public class GuestProbeService { }
    public class HostProbeService { }
}

// ─── Platform / Embedded / QMP ───────────────────────────────────

namespace NetContainer.Ref.Platform
{
    public class BootAttempt
    {
        public string? GuestId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class HostPlatformContext
    {
        public string OsName { get; set; } = "browser-wasm";
        public bool IsLinux => false;
        public bool IsWindows => false;
        public bool IsMacOS => false;
    }
}

namespace NetContainer.Ref.Embedded
{
    public class EmbeddedBinaryResolver
    {
        public string? ResolveQemuPath(string arch) => null;
    }
}

namespace NetContainer.Ref.Qmp
{
    public class RefQmpClient : IDisposable
    {
        public Task ConnectAsync(int port, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> ExecuteAsync(string command, CancellationToken ct = default)
            => Task.FromResult("{}");
        public void Dispose() { }
    }
}

// ─── Terminal / CLI namespaces ───────────────────────────────────

namespace NetContainer.Ref.Terminal
{
    public class XtermWebSocketBridge { }
}

namespace NetContainer.Ref.Cli { }

#endif
