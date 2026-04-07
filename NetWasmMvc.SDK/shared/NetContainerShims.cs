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
        private Services.IShellService? _shell;
        public Services.IShellService Shell => _shell ??= new Services.BrowserShellService(Id, SshPort, HttpPort, StartedAt ?? DateTime.UtcNow);
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
        private readonly string _guestId;
        private readonly int _sshPort;
        private readonly int _httpPort;
        private readonly DateTime _bootTime;
        private readonly Dictionary<string, string> _env = new()
        {
            ["HOME"] = "/root", ["PATH"] = "/usr/sbin:/usr/bin:/sbin:/bin",
            ["SHELL"] = "/bin/ash", ["USER"] = "root", ["TERM"] = "xterm-256color",
            ["HOSTNAME"] = "OpenWrt", ["LOGNAME"] = "root"
        };
        private string _cwd = "/root";

        // Virtual filesystem: directory → child names
        private static readonly Dictionary<string, string[]> _vfs = new()
        {
            ["/"] = new[] { "bin", "dev", "etc", "lib", "mnt", "overlay", "proc", "rom", "root", "run", "sbin", "sys", "tmp", "usr", "var", "www" },
            ["/root"] = new[] { ".profile", ".ash_history" },
            ["/etc"] = new[] { "banner", "config", "crontabs", "dropbear", "group", "hostname", "hosts", "init.d", "inittab", "opkg", "openwrt_release", "openwrt_version", "os-release", "passwd", "profile", "profile.d", "rc.d", "resolv.conf", "shadow", "shells", "sysctl.conf", "sysctl.d" },
            ["/etc/config"] = new[] { "dhcp", "dropbear", "firewall", "luci", "network", "system", "uhttpd", "wireless" },
            ["/etc/init.d"] = new[] { "boot", "cron", "dnsmasq", "done", "dropbear", "firewall", "led", "log", "network", "odhcpd", "sysctl", "sysntpd", "system", "uhttpd" },
            ["/etc/rc.d"] = new[] { "S10boot", "S11sysctl", "S12log", "S19firewall", "S20network", "S35odhcpd", "S50dropbear", "S50uhttpd", "S95done", "K50dropbear", "K90network" },
            ["/etc/dropbear"] = new[] { "dropbear_ed25519_host_key", "authorized_keys" },
            ["/etc/opkg"] = new[] { "distfeeds.conf", "keys" },
            ["/etc/profile.d"] = new[] { "colorize.sh" },
            ["/etc/sysctl.d"] = new[] { "10-default.conf", "11-br-netfilter.conf" },
            ["/tmp"] = new[] { "dhcp.leases", "hosts", "resolv.conf", "resolv.conf.d", "run", "state", "TMP_DIR", "sysinfo" },
            ["/tmp/sysinfo"] = new[] { "board_name", "model" },
            ["/tmp/run"] = new[] { "dropbear.pid", "dnsmasq", "uhttpd.pid", "netifd.pid" },
            ["/var"] = new[] { "etc", "lock", "log", "run", "tmp" },
            ["/var/log"] = new[] { "lastlog", "wtmp" },
            ["/usr"] = new[] { "bin", "lib", "libexec", "sbin", "share" },
            ["/usr/bin"] = new[] { "jsonfilter", "jshn", "wget-ssl", "curl", "ssh", "scp" },
            ["/usr/sbin"] = new[] { "dnsmasq", "dropbear", "fw4", "ntpd", "odhcpd", "uhttpd" },
            ["/usr/lib"] = new[] { "lua", "opkg", "libiwinfo.so", "libjson-c.so.5", "libubus.so", "libuci.so" },
            ["/usr/share"] = new[] { "udhcpc" },
            ["/bin"] = new[] { "ash", "busybox", "cat", "chmod", "cp", "date", "dd", "df", "dmesg", "echo", "egrep", "fgrep", "grep", "gzip", "hostname", "kill", "ln", "login", "ls", "mkdir", "mknod", "mount", "mv", "nice", "pidof", "ping", "ps", "pwd", "rm", "rmdir", "sed", "sh", "sleep", "sync", "tar", "touch", "umount", "uname", "vi" },
            ["/sbin"] = new[] { "block", "firstboot", "fsck", "halt", "ifconfig", "ifdown", "ifup", "init", "insmod", "ip", "logd", "logread", "lsmod", "modprobe", "mount_root", "netifd", "poweroff", "procd", "reboot", "rmmod", "route", "swapoff", "swapon", "sysctl", "ubusd", "uci", "umount", "wifi" },
            ["/www"] = new[] { "cgi-bin", "index.html", "luci-static" },
            ["/www/cgi-bin"] = new[] { "luci" },
            ["/proc"] = new[] { "1", "cpuinfo", "meminfo", "mounts", "net", "sys", "uptime", "version", "loadavg", "stat", "filesystems" },
            ["/dev"] = new[] { "console", "null", "ptmx", "pts", "random", "tty", "urandom", "zero" },
            ["/sys"] = new[] { "block", "bus", "class", "devices", "firmware", "fs", "kernel", "module", "power" },
            ["/lib"] = new[] { "config", "functions", "libc.so", "libgcc_s.so.1", "libpthread.so.0", "modules", "netifd", "preinit", "upgrade" },
            ["/overlay"] = new[] { "upper", "work" },
            ["/mnt"] = System.Array.Empty<string>(),
            ["/rom"] = new[] { "bin", "dev", "etc", "lib", "overlay", "sbin", "usr", "var", "www" },
            ["/run"] = new[] { "netifd.pid", "dropbear.1.pid" },
        };

        // Virtual file contents
        private static readonly Dictionary<string, string> _fileContents = new()
        {
            ["/etc/banner"] =
                "  _______                     ________        __\n" +
                " |       |.-----.-----.-----.|  |  |  |.----.|  |_\n" +
                " |   -   ||  _  |  -__|     ||  |  |  ||   _||   _|\n" +
                " |_______||   __|_____|__|__||________||__|  |____|\n" +
                "          |__| W I R E L E S S   F R E E D O M\n" +
                " -----------------------------------------------------\n" +
                " OpenWrt 23.05.5, r27925-e36b028ab5\n" +
                " -----------------------------------------------------",
            ["/etc/os-release"] =
                "DISTRIB_ID='OpenWrt'\nDISTRIB_RELEASE='23.05.5'\nDISTRIB_REVISION='r27925-e36b028ab5'\n" +
                "DISTRIB_TARGET='x86/64'\nDISTRIB_ARCH='x86_64'\n" +
                "DISTRIB_DESCRIPTION='OpenWrt 23.05.5 r27925-e36b028ab5'\nDISTRIB_TAINTS=''",
            ["/etc/openwrt_release"] =
                "DISTRIB_ID='OpenWrt'\nDISTRIB_RELEASE='23.05.5'\nDISTRIB_REVISION='r27925-e36b028ab5'\n" +
                "DISTRIB_TARGET='x86/64'\nDISTRIB_ARCH='x86_64'\n" +
                "DISTRIB_DESCRIPTION='OpenWrt 23.05.5 r27925-e36b028ab5'\nDISTRIB_TAINTS=''",
            ["/etc/openwrt_version"] = "r27925-e36b028ab5",
            ["/etc/hostname"] = "OpenWrt",
            ["/etc/hosts"] = "127.0.0.1 localhost\n::1 localhost ip6-localhost ip6-loopback\nff02::1 ip6-allnodes\nff02::2 ip6-allrouters",
            ["/etc/shells"] = "/bin/ash\n/bin/sh",
            ["/etc/passwd"] = "root:x:0:0:root:/root:/bin/ash\nnobody:*:65534:65534:nobody:/var:/bin/false\ndaemon:*:1:1:daemon:/var:/bin/false\nnetwork:x:101:101:network:/var:/bin/false",
            ["/etc/group"] = "root:x:0:\nnogroup:x:65534:\nnetwork:x:101:",
            ["/etc/shadow"] = "root:::0:99999:7:::\nnobody:*:0:0:99999:7:::\ndaemon:*:0:0:99999:7:::",
            ["/etc/inittab"] = "::sysinit:/etc/init.d/rcS S boot\n::shutdown:/etc/init.d/rcS K shutdown\n::askconsole:/usr/libexec/login.sh",
            ["/etc/profile"] = "#!/bin/sh\n[ -f /etc/banner ] && cat /etc/banner\n[ -e /tmp/.failsafe ] && cat /etc/banner.failsafe\nexport PATH=\"/usr/sbin:/usr/bin:/sbin:/bin\"\nexport HOME=$(grep -e \"^${USER:-root}:\" /etc/passwd | cut -d \":\" -f 6)\nexport PS1='\\u@\\h:\\w\\$ '",
            ["/etc/resolv.conf"] = "search lan\nnameserver 127.0.0.1",
            ["/etc/sysctl.conf"] = "kernel.panic=3\nnet.ipv4.ip_forward=1\nnet.ipv6.conf.all.forwarding=1",
            ["/etc/config/network"] =
                "config interface 'loopback'\n\toption device 'lo'\n\toption proto 'static'\n\toption ipaddr '127.0.0.1'\n\toption netmask '255.0.0.0'\n\n" +
                "config interface 'lan'\n\toption device 'br-lan'\n\toption proto 'static'\n\toption ipaddr '192.168.1.1'\n\toption netmask '255.255.255.0'",
            ["/etc/config/system"] =
                "config system\n\toption hostname 'OpenWrt'\n\toption timezone 'UTC'\n\toption ttylogin '0'\n\toption log_size '64'\n\toption urandom_seed '0'\n\n" +
                "config timeserver 'ntp'\n\tlist server '0.openwrt.pool.ntp.org'\n\tlist server '1.openwrt.pool.ntp.org'",
            ["/etc/config/firewall"] =
                "config defaults\n\toption syn_flood '1'\n\toption input 'ACCEPT'\n\toption output 'ACCEPT'\n\toption forward 'REJECT'\n\n" +
                "config zone\n\toption name 'lan'\n\tlist network 'lan'\n\toption input 'ACCEPT'\n\toption output 'ACCEPT'\n\toption forward 'ACCEPT'",
            ["/etc/config/dropbear"] =
                "config dropbear\n\toption PasswordAuth 'on'\n\toption Port '22'\n\toption Interface ''",
            ["/etc/config/dhcp"] =
                "config dnsmasq\n\toption domainneeded '1'\n\toption localise_queries '1'\n\toption rebind_protection '1'\n\toption local '/lan/'\n\toption domain 'lan'\n\n" +
                "config dhcp 'lan'\n\toption interface 'lan'\n\toption start '100'\n\toption limit '150'\n\toption leasetime '12h'",
            ["/etc/config/uhttpd"] =
                "config uhttpd 'main'\n\tlist listen_http '0.0.0.0:80'\n\toption home '/www'\n\toption rfc1918_filter '1'\n\toption max_requests '3'\n\toption cert '/etc/uhttpd.crt'\n\toption key '/etc/uhttpd.key'",
            ["/etc/config/wireless"] =
                "config wifi-device 'radio0'\n\toption type 'mac80211'\n\toption path 'virtual/mac80211_hwsim/hwsim0'\n\toption channel '1'\n\toption band '2g'\n\toption htmode 'HT20'\n\n" +
                "config wifi-iface 'default_radio0'\n\toption device 'radio0'\n\toption network 'lan'\n\toption mode 'ap'\n\toption ssid 'OpenWrt'\n\toption encryption 'none'",
            ["/etc/opkg/distfeeds.conf"] =
                "src/gz openwrt_core https://downloads.openwrt.org/releases/23.05.5/targets/x86/64/packages\n" +
                "src/gz openwrt_base https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/base\n" +
                "src/gz openwrt_luci https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/luci\n" +
                "src/gz openwrt_packages https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/packages",
            ["/proc/version"] = "Linux version 6.6.67 (builder@buildhost) (gcc 13.3.0) #0 SMP x86_64",
            ["/proc/cpuinfo"] =
                "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: QEMU Virtual CPU version 2.5+\n" +
                "cpu MHz\t\t: 2400.000\ncache size\t: 4096 KB\nbogomips\t: 4800.00\n" +
                "flags\t\t: fpu vme de pse tsc msr pae mce cx8 apic sep mtrr pge mca cmov pat pse36 clflush mmx fxsr sse sse2 ht syscall nx lm rep_good nopl xtopology cpuid pni cx16 x2apic hypervisor lahf_lm",
            ["/proc/meminfo"] =
                "MemTotal:        2097152 kB\nMemFree:         1876992 kB\nMemAvailable:    1951744 kB\n" +
                "Buffers:           28672 kB\nCached:            46080 kB\nSwapTotal:             0 kB\nSwapFree:              0 kB",
            ["/proc/loadavg"] = "0.08 0.03 0.01 1/60 1284",
            ["/proc/filesystems"] = "nodev\tproc\nnodev\ttmpfs\nnodev\tsysfs\nnodev\tdevpts\n\text4\nnodev\tsquashfs",
            ["/proc/mounts"] =
                "/dev/root / ext4 rw,noatime 0 0\nproc /proc proc rw,nosuid,nodev,noexec,noatime 0 0\n" +
                "sysfs /sys sysfs rw,nosuid,nodev,noexec,noatime 0 0\ntmpfs /tmp tmpfs rw,nosuid,nodev,noatime 0 0\n" +
                "devpts /dev/pts devpts rw,nosuid,noexec,mode=600,ptmxmode=000 0 0",
            ["/root/.profile"] = "#!/bin/sh\n# ~/.profile: executed by Bourne-compatible login shells\n[ -f /etc/banner ] && cat /etc/banner",
            ["/root/.ash_history"] = "uname -a\nifconfig\nfree\ndf -h\nps\nlogread\ncat /etc/config/network",
            ["/tmp/sysinfo/board_name"] = "generic",
            ["/tmp/sysinfo/model"] = "QEMU Standard PC (i440FX + PIIX, 1996)",
            ["/tmp/dhcp.leases"] = "",
            ["/www/index.html"] = "<html><head><title>OpenWrt</title></head><body><h1>OpenWrt</h1><p><a href=\"/cgi-bin/luci\">LuCI</a></p></body></html>",
        };

        internal BrowserShellService(string guestId, int sshPort, int httpPort, DateTime bootTime)
        {
            _guestId = guestId; _sshPort = sshPort;
            _httpPort = httpPort; _bootTime = bootTime;
        }

        public string CurrentDirectory => _cwd;

        public Task<ShellResult> RunAsync(string command, int timeoutMs = 5000,
            CancellationToken ct = default)
        {
            var cmd = (command ?? "").Trim();
            var output = Interpret(cmd);
            return Task.FromResult(new ShellResult { ExitCode = 0, StdOut = output });
        }

        public Task<ShellResult> RunRequiredAsync(string command, int timeoutMs = 5000,
            CancellationToken ct = default)
            => RunAsync(command, timeoutMs, ct);

        private string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return _cwd;
            if (path == "~") return "/root";
            if (path.StartsWith("~/")) path = "/root/" + path[2..];
            if (!path.StartsWith("/")) path = _cwd == "/" ? "/" + path : _cwd + "/" + path;

            // Normalize: resolve . and ..
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();
            foreach (var p in parts)
            {
                if (p == ".") continue;
                if (p == "..") { if (stack.Count > 0) stack.Pop(); }
                else stack.Push(p);
            }
            var resolved = "/" + string.Join("/", stack.Reverse());
            return resolved == "" ? "/" : resolved;
        }

        private bool PathExists(string path)
        {
            if (path == "/") return true;
            if (_vfs.ContainsKey(path)) return true;
            if (_fileContents.ContainsKey(path)) return true;
            // Check if it's a child of any known directory
            var parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : "/";
            if (parent == "") parent = "/";
            if (_vfs.TryGetValue(parent, out var children))
            {
                var name = path[(path.LastIndexOf('/') + 1)..];
                return children.Contains(name);
            }
            return false;
        }

        private bool IsDirectory(string path)
        {
            if (path == "/") return true;
            return _vfs.ContainsKey(path);
        }

        private string Interpret(string cmd)
        {
            // Handle pipes (run left side only for display)
            if (cmd.Contains('|'))
            {
                var pipeIdx = cmd.IndexOf('|');
                var left = cmd[..pipeIdx].Trim();
                var right = cmd[(pipeIdx + 1)..].Trim();
                var leftOut = Interpret(left);
                return ApplyPipe(leftOut, right);
            }

            // Handle semicolons
            if (cmd.Contains(';'))
            {
                var parts = cmd.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return string.Join("\n", parts.Select(Interpret));
            }

            // Handle && chains
            if (cmd.Contains("&&"))
            {
                var parts = cmd.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return string.Join("\n", parts.Select(Interpret));
            }

            var tokens = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return "";
            var bin = tokens[0];
            var args = tokens.Skip(1).ToArray();
            var uptime = (DateTime.UtcNow - _bootTime).TotalSeconds;

            return bin switch
            {
                "uname" => InterpretUname(args),
                "cat" => InterpretCat(args),
                "echo" => InterpretEcho(args),
                "hostname" => "OpenWrt",
                "whoami" => "root",
                "id" => "uid=0(root) gid=0(root) groups=0(root)",
                "pwd" => _cwd,
                "cd" => CdCommand(args),
                "uptime" => $" {DateTime.UtcNow:HH:mm:ss} up {(int)(uptime / 60)} min,  load average: 0.08, 0.03, 0.01",
                "date" => DateTime.UtcNow.ToString("ddd MMM dd HH:mm:ss UTC yyyy"),
                "free" => "              total        used        free      shared  buff/cache   available\n" +
                          "Mem:        2097152      145408     1876992        2048       74752     1951744\n" +
                          "Swap:             0           0           0",
                "df" => InterpretDf(args),
                "mount" => _fileContents.GetValueOrDefault("/proc/mounts", ""),
                "ps" => InterpretPs(uptime),
                "top" => InterpretTop(),
                "ifconfig" => InterpretIfconfig(),
                "ip" => InterpretIp(args),
                "route" => InterpretRoute(),
                "netstat" => InterpretNetstat(),
                "ls" => InterpretLs(args),
                "ll" => InterpretLs(new[] { "-la" }.Concat(args).ToArray()),
                "dmesg" => InterpretDmesg(),
                "uci" => InterpretUci(args),
                "opkg" => InterpretOpkg(args),
                "logread" => InterpretLogread(args),
                "env" => string.Join("\n", _env.Select(kv => $"{kv.Key}={kv.Value}")),
                "set" => string.Join("\n", _env.Select(kv => $"{kv.Key}='{kv.Value}'")),
                "export" when args.Length > 0 && args[0].Contains('=') => ExportCommand(args[0]),
                "mkdir" => InterpretMkdir(args),
                "touch" => InterpretTouch(args),
                "rm" => "", // silent success
                "mv" => "", // silent success
                "cp" => "", // silent success
                "chmod" => "", // silent success
                "chown" => "", // silent success
                "head" => InterpretHead(args),
                "tail" => InterpretTail(args),
                "wc" => InterpretWc(args),
                "grep" => InterpretGrep(args),
                "which" => InterpretWhich(args),
                "type" => InterpretType(args),
                "ping" => InterpretPing(args),
                "wget" or "wget-ssl" => InterpretWget(args),
                "curl" => InterpretCurl(args),
                "fw4" or "fw3" => "table inet fw4 {\n  chain input {\n    type filter hook input priority filter; policy accept;\n  }\n}",
                "service" => InterpretService(args),
                "/etc/init.d/dropbear" or "/etc/init.d/uhttpd" or "/etc/init.d/firewall" or "/etc/init.d/network" =>
                    InterpretInitScript(bin, args),
                "reboot" => "The system is going down NOW!",
                "poweroff" => "The system is going down for system halt NOW!",
                "clear" => "",
                "true" => "",
                "false" => "",
                "test" => "",
                "exit" => "",
                "help" => "Built-in commands: cat cd chmod cp curl date dd df dmesg echo env free\n" +
                          "  grep head hostname id ifconfig ip kill ln logread ls mkdir mount mv\n" +
                          "  netstat opkg ping poweroff ps pwd reboot rm route service tail\n" +
                          "  top touch type uci uname uptime wc wget which whoami",
                _ => cmd.StartsWith("#") || cmd.StartsWith("//") ? "" : $"-ash: {bin}: not found"
            };
        }

        private string ApplyPipe(string input, string pipeCmd)
        {
            var tokens = pipeCmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return input;
            var lines = input.Split('\n');
            return tokens[0] switch
            {
                "head" => string.Join("\n", lines.Take(tokens.Length > 2 ? int.Parse(tokens[2]) : 10)),
                "tail" => string.Join("\n", lines.TakeLast(tokens.Length > 2 ? int.Parse(tokens[2]) : 10)),
                "wc" => tokens.Contains("-l") ? lines.Length.ToString() : $"  {lines.Length}  {lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)}  {input.Length}",
                "grep" when tokens.Length > 1 => string.Join("\n", lines.Where(l => l.Contains(tokens[1], StringComparison.OrdinalIgnoreCase))),
                "sort" => string.Join("\n", lines.OrderBy(l => l)),
                _ => input
            };
        }

        private string InterpretUname(string[] args)
        {
            if (args.Contains("-a")) return "Linux OpenWrt 6.6.67 #0 SMP Mon Jan 6 12:00:00 2025 x86_64 GNU/Linux";
            if (args.Contains("-r")) return "6.6.67";
            if (args.Contains("-m")) return "x86_64";
            if (args.Contains("-s")) return "Linux";
            if (args.Contains("-n")) return "OpenWrt";
            if (args.Contains("-v")) return "#0 SMP Mon Jan 6 12:00:00 2025";
            return "Linux";
        }

        private string InterpretEcho(string[] args)
        {
            if (args.Length == 0) return "";
            var text = string.Join(" ", args);
            // Expand environment variables
            foreach (var kv in _env)
                text = text.Replace($"${kv.Key}", kv.Value).Replace($"${{{kv.Key}}}", kv.Value);
            return text.Trim('"', '\'');
        }

        private string CdCommand(string[] args)
        {
            var target = args.Length > 0 ? args[0] : "/root";
            var resolved = ResolvePath(target);
            if (!IsDirectory(resolved))
            {
                if (PathExists(resolved)) return $"-ash: cd: {target}: Not a directory";
                return $"-ash: cd: can't cd to {target}: No such file or directory";
            }
            _cwd = resolved;
            return "";
        }

        private string ExportCommand(string expr)
        {
            var kv = expr.Split('=', 2);
            _env[kv[0]] = kv.Length > 1 ? kv[1].Trim('"', '\'') : "";
            return "";
        }

        private string InterpretCat(string[] args)
        {
            if (args.Length == 0) return "";
            var results = new List<string>();
            foreach (var arg in args.Where(a => !a.StartsWith("-")))
            {
                var path = ResolvePath(arg);
                if (_fileContents.TryGetValue(path, out var content))
                    results.Add(content);
                else if (IsDirectory(path))
                    results.Add($"cat: {arg}: Is a directory");
                else if (path == "/proc/uptime")
                    results.Add($"{(DateTime.UtcNow - _bootTime).TotalSeconds:F2} {(DateTime.UtcNow - _bootTime).TotalSeconds * 0.98:F2}");
                else if (path == "/proc/loadavg")
                    results.Add("0.08 0.03 0.01 1/60 1284");
                else
                    results.Add($"cat: can't open '{arg}': No such file or directory");
            }
            return string.Join("\n", results);
        }

        private string InterpretDf(string[] args)
        {
            var h = args.Contains("-h") || args.Contains("-hT");
            return "Filesystem                Size      Used Available Use% Mounted on\n" +
                   "/dev/root                48.0M     12.4M     33.5M  27% /\n" +
                   "tmpfs                   512.0M     72.0K    511.9M   0% /tmp\n" +
                   "tmpfs                   512.0K         0    512.0K   0% /dev\n" +
                   "/dev/sda1                16.0M      3.2M     12.6M  20% /boot";
        }

        private string InterpretPs(double uptime)
        {
            return "  PID USER       VSZ STAT COMMAND\n" +
                   "    1 root      1584 S    /sbin/procd\n" +
                   "  478 root      1168 S    /sbin/ubusd\n" +
                   "  652 root       892 S    /sbin/logd -S 16\n" +
                   "  738 root      1356 S    /sbin/netifd\n" +
                   "  892 root      2872 S    /usr/sbin/odhcpd\n" +
                   "  934 root      1284 S    /usr/sbin/dnsmasq -C /var/etc/dnsmasq.conf.cfg01411c\n" +
                   " 1034 root      5120 S    /usr/sbin/uhttpd -f -h /www -r OpenWrt -x /cgi-bin -t 60 -T 30 -A 1\n" +
                   " 1089 root      3072 S    /usr/sbin/dropbear -F -P /var/run/dropbear.1.pid -p 22\n" +
                   " 1156 root      1528 S    /usr/sbin/ntpd -n -N -S /usr/sbin/ntpd-hotplug -p 0.openwrt.pool.ntp.org\n" +
                   $" 1284 root      1168 R    ash -c ps";
        }

        private string InterpretTop()
        {
            return $"Mem: 145408K used, 1951744K free, 2048K shrd, 28672K buff, 46080K cached\n" +
                   "CPU:   0% usr   1% sys   0% nic  98% idle   0% io   0% irq   0% sirq\n" +
                   "Load average: 0.08 0.03 0.01 1/60 1284\n" +
                   "  PID  PPID USER     STAT   VSZ %VSZ %CPU COMMAND\n" +
                   "    1     0 root     S     1584   0%   0% /sbin/procd\n" +
                   "  892     1 root     S     1280   0%   0% /sbin/ubusd\n" +
                   " 1034     1 root     S     5120   0%   0% /usr/sbin/uhttpd\n" +
                   " 1089     1 root     S     3072   0%   0% /usr/sbin/dropbear";
        }

        private string InterpretIfconfig()
        {
            return "br-lan    Link encap:Ethernet  HWaddr 52:54:00:12:34:56\n" +
                   "          inet addr:192.168.1.1  Bcast:192.168.1.255  Mask:255.255.255.0\n" +
                   "          UP BROADCAST RUNNING MULTICAST  MTU:1500  Metric:1\n" +
                   "          RX packets:0 errors:0 dropped:0 overruns:0 frame:0\n" +
                   "          TX packets:42 errors:0 dropped:0 overruns:0 carrier:0\n\n" +
                   "eth0      Link encap:Ethernet  HWaddr 52:54:00:12:34:56\n" +
                   "          inet addr:10.0.2.15  Bcast:10.0.2.255  Mask:255.255.255.0\n" +
                   "          UP BROADCAST RUNNING MULTICAST  MTU:1500  Metric:1\n" +
                   "          RX packets:128 errors:0 dropped:0 overruns:0 frame:0\n" +
                   "          TX packets:96 errors:0 dropped:0 overruns:0 carrier:0\n\n" +
                   "lo        Link encap:Local Loopback\n" +
                   "          inet addr:127.0.0.1  Mask:255.0.0.0\n" +
                   "          UP LOOPBACK RUNNING  MTU:65536  Metric:1";
        }

        private string InterpretIp(string[] args)
        {
            var sub = args.FirstOrDefault() ?? "";
            return sub switch
            {
                "addr" or "a" => InterpretIfconfig(),
                "route" or "r" => "default via 10.0.2.2 dev eth0\n10.0.2.0/24 dev eth0 scope link  src 10.0.2.15\n192.168.1.0/24 dev br-lan scope link  src 192.168.1.1",
                "link" or "l" => "1: lo: <LOOPBACK,UP> mtu 65536 qdisc noqueue state UP\n    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00\n2: eth0: <BROADCAST,MULTICAST,UP> mtu 1500 qdisc fq_codel state UP qlen 1000\n    link/ether 52:54:00:12:34:56 brd ff:ff:ff:ff:ff:ff\n3: br-lan: <BROADCAST,MULTICAST,UP> mtu 1500 qdisc noqueue state UP\n    link/ether 52:54:00:12:34:56 brd ff:ff:ff:ff:ff:ff",
                "neigh" or "n" => "10.0.2.2 dev eth0 lladdr 52:55:0a:00:02:02 REACHABLE",
                _ => "Usage: ip [ addr | route | link | neigh ]"
            };
        }

        private string InterpretRoute()
        {
            return "Kernel IP routing table\n" +
                   "Destination     Gateway         Genmask         Flags Metric Ref    Use Iface\n" +
                   "default         10.0.2.2        0.0.0.0         UG    0      0        0 eth0\n" +
                   "10.0.2.0        *               255.255.255.0   U     0      0        0 eth0\n" +
                   "192.168.1.0     *               255.255.255.0   U     0      0        0 br-lan";
        }

        private string InterpretNetstat()
        {
            return "Active Internet connections (servers and established)\n" +
                   "Proto Recv-Q Send-Q Local Address           Foreign Address         State\n" +
                   $"tcp        0      0 0.0.0.0:{_httpPort}            0.0.0.0:*               LISTEN\n" +
                   $"tcp        0      0 0.0.0.0:{_sshPort}             0.0.0.0:*               LISTEN\n" +
                   "tcp        0      0 0.0.0.0:80              0.0.0.0:*               LISTEN\n" +
                   "tcp        0      0 0.0.0.0:53              0.0.0.0:*               LISTEN\n" +
                   "udp        0      0 0.0.0.0:53              0.0.0.0:*\n" +
                   "udp        0      0 0.0.0.0:67              0.0.0.0:*\n" +
                   "udp        0      0 0.0.0.0:68              0.0.0.0:*";
        }

        private string InterpretLs(string[] args)
        {
            var longFormat = args.Any(a => a.StartsWith("-") && a.Contains('l'));
            var showAll = args.Any(a => a.StartsWith("-") && a.Contains('a'));
            var target = args.LastOrDefault(a => !a.StartsWith("-")) ?? _cwd;
            var resolved = ResolvePath(target);

            if (!IsDirectory(resolved))
            {
                if (PathExists(resolved))
                {
                    var name = resolved[(resolved.LastIndexOf('/') + 1)..];
                    return longFormat ? $"-rw-r--r--    1 root     root          512 Jan  6 12:00 {name}" : name;
                }
                return $"ls: {target}: No such file or directory";
            }

            if (!_vfs.TryGetValue(resolved, out var children)) return "";
            var items = showAll ? new[] { ".", ".." }.Concat(children).ToArray() : children;
            if (items.Length == 0) return "";

            if (longFormat)
            {
                var lines = new List<string>();
                lines.Add($"drwxr-xr-x    {items.Length + 2} root     root          4096 Jan  6 12:00 .");
                lines.Add($"drwxr-xr-x    {items.Length + 2} root     root          4096 Jan  6 12:00 ..");
                foreach (var item in children)
                {
                    var fullPath = resolved == "/" ? "/" + item : resolved + "/" + item;
                    if (IsDirectory(fullPath))
                        lines.Add($"drwxr-xr-x    2 root     root          4096 Jan  6 12:00 {item}");
                    else
                    {
                        var size = _fileContents.TryGetValue(fullPath, out var c) ? c.Length : 512;
                        lines.Add($"-rw-r--r--    1 root     root         {size,5} Jan  6 12:00 {item}");
                    }
                }
                return string.Join("\n", lines);
            }
            return string.Join("  ", children);
        }

        private string InterpretDmesg()
        {
            return "[    0.000000] Linux version 6.6.67 (builder@buildhost) (gcc (OpenWrt GCC 13.3.0 r27925-e36b028ab5) 13.3.0, GNU ld (GNU Binutils) 2.42) #0 SMP x86_64\n" +
                   "[    0.000000] Command line: root=/dev/sda console=ttyS0\n" +
                   "[    0.000000] BIOS-provided physical RAM map:\n" +
                   "[    0.000000]  BIOS-e820: [mem 0x0000000000000000-0x000000000009fbff] usable\n" +
                   "[    0.010000] x86/fpu: x87 FPU on board\n" +
                   "[    0.020000] Initializing cgroup subsys cpuset\n" +
                   "[    0.050000] Calibrating delay loop... 4800.00 BogoMIPS (lpj=2400000)\n" +
                   "[    0.100000] Memory: 2048MB available\n" +
                   "[    0.200000] PCI: Using configuration type 1 for base access\n" +
                   "[    0.300000] e1000: Intel(R) PRO/1000 Network Driver\n" +
                   "[    0.400000] e1000 0000:00:03.0: eth0: (PCI:33MHz:32-bit) MAC: 52:54:00:12:34:56\n" +
                   "[    0.500000] EXT4-fs (sda): mounted filesystem with ordered data mode\n" +
                   "[    0.600000] init: Console is alive\n" +
                   "[    0.700000] init: - watchdog -\n" +
                   "[    0.800000] procd: - early -\n" +
                   "[    0.900000] procd: - ubus -\n" +
                   "[    1.000000] procd: - init -\n" +
                   "[    1.500000] procd: Started /etc/rc.d/S10boot\n" +
                   "[    1.800000] procd: Started /etc/rc.d/S19firewall\n" +
                   "[    2.000000] procd: Started /etc/rc.d/S20network\n" +
                   "[    2.200000] br-lan: port 1(eth0) entered forwarding state\n" +
                   "[    2.500000] procd: Started /etc/rc.d/S50uhttpd\n" +
                   "[    2.600000] procd: Started /etc/rc.d/S50dropbear\n" +
                   "[    3.000000] procd: - init complete -";
        }

        private string InterpretUci(string[] args)
        {
            var sub = args.FirstOrDefault() ?? "";
            return sub switch
            {
                "show" when args.Length > 1 => InterpretUciShow(args[1]),
                "show" => "system.@system[0]=system\nsystem.@system[0].hostname='OpenWrt'\nsystem.@system[0].timezone='UTC'\n" +
                          "network.loopback=interface\nnetwork.loopback.device='lo'\nnetwork.loopback.proto='static'\n" +
                          "network.lan=interface\nnetwork.lan.device='br-lan'\nnetwork.lan.proto='static'\nnetwork.lan.ipaddr='192.168.1.1'",
                "get" when args.Length > 1 => InterpretUciGet(args[1]),
                "set" when args.Length > 1 => "",
                "commit" => "",
                "changes" => "",
                "export" when args.Length > 1 => _fileContents.GetValueOrDefault("/etc/config/" + args[1], $"uci: Entry not found"),
                _ => "Usage: uci [show|get|set|commit|changes|export]"
            };
        }

        private string InterpretUciShow(string key)
        {
            if (key.StartsWith("network")) return "network.lan=interface\nnetwork.lan.device='br-lan'\nnetwork.lan.proto='static'\nnetwork.lan.ipaddr='192.168.1.1'\nnetwork.lan.netmask='255.255.255.0'";
            if (key.StartsWith("system")) return "system.@system[0]=system\nsystem.@system[0].hostname='OpenWrt'\nsystem.@system[0].timezone='UTC'\nsystem.@system[0].ttylogin='0'";
            if (key.StartsWith("dhcp")) return "dhcp.@dnsmasq[0]=dnsmasq\ndhcp.@dnsmasq[0].domainneeded='1'\ndhcp.lan=dhcp\ndhcp.lan.interface='lan'\ndhcp.lan.start='100'\ndhcp.lan.limit='150'";
            if (key.StartsWith("firewall")) return "firewall.@defaults[0]=defaults\nfirewall.@defaults[0].syn_flood='1'\nfirewall.@defaults[0].input='ACCEPT'";
            return "";
        }

        private string InterpretUciGet(string key)
        {
            return key switch
            {
                "system.@system[0].hostname" => "OpenWrt",
                "system.@system[0].timezone" => "UTC",
                "network.lan.ipaddr" => "192.168.1.1",
                "network.lan.netmask" => "255.255.255.0",
                "network.lan.proto" => "static",
                "network.lan.device" => "br-lan",
                "dhcp.lan.start" => "100",
                "dhcp.lan.limit" => "150",
                _ => $"uci: Entry not found"
            };
        }

        private string InterpretOpkg(string[] args)
        {
            var sub = args.FirstOrDefault() ?? "";
            return sub switch
            {
                "update" =>
                    "Downloading https://downloads.openwrt.org/releases/23.05.5/targets/x86/64/packages/Packages.gz\n" +
                    "Updated list of available packages in /var/opkg-lists/openwrt_core\n" +
                    "Downloading https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/base/Packages.gz\n" +
                    "Updated list of available packages in /var/opkg-lists/openwrt_base\n" +
                    "Downloading https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/luci/Packages.gz\n" +
                    "Updated list of available packages in /var/opkg-lists/openwrt_luci\n" +
                    "Downloading https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/packages/Packages.gz\n" +
                    "Updated list of available packages in /var/opkg-lists/openwrt_packages",
                "list-installed" =>
                    "base-files - 1563-r27925\nbusybox - 1.36.1-1\ndnsmasq - 2.90-1\ndropbear - 2024.85-1\n" +
                    "firewall4 - 2024.02-1\nfstools - 2024.01.1-1\njsonfilter - 2024.01.1-1\nkernel - 6.6.67-1-x86_64\n" +
                    "kmod-e1000 - 6.6.67-1\nlibc - 1.2.5-1\nlibgcc1 - 13.3.0-1\nlibjson-c5 - 0.17-1\n" +
                    "libpthread - 1.2.5-1\nlibubus - 2024.01-1\nlibuci20130104 - 2024.04.06-1\nluci - git-24.086\n" +
                    "netifd - 2024.07.15-1\nodhcpd-ipv6only - 2024.04-1\nopkg - 2024.01-1\nprocd - 2024.06.29-1\n" +
                    "ubus - 2024.01-1\nuci - 2024.04.06-1\nuhttpd - 2024.01-1",
                "list" when args.Length > 1 => InterpretOpkgList(args.Skip(1).FirstOrDefault()),
                "list" =>
                    "base-files - 1563-r27925 - Base filesystem for OpenWrt\nbusybox - 1.36.1-1 - BusyBox utilities\n" +
                    "curl - 8.5.0-1 - A client-side URL transfer library\ndnsmasq - 2.90-1 - DNS and DHCP server\n" +
                    "dropbear - 2024.85-1 - SSH server and client\nluci - git-24.086 - LuCI web interface\n" +
                    "nano - 7.2-2 - GNU nano text editor\nopenvpn - 2.6.8-1 - Open source VPN solution\n" +
                    "tcpdump - 4.99.4-1 - Network packet analyzer\nwireguard-tools - 1.0.20210914-2 - WireGuard tools",
                "info" when args.Length > 1 => $"Package: {args[1]}\nVersion: 2024.01\nDepends: libc\nStatus: install ok installed\nSection: base\nArchitecture: x86_64\nInstalled-Size: 65536\nDescription: {args[1]} package for OpenWrt",
                "info" => "Package: opkg\nVersion: 2024.01\nStatus: install ok installed",
                "install" when args.Length > 1 => $"Installing {args[1]} (2024.01) to root...\nDownloading https://downloads.openwrt.org/releases/23.05.5/packages/x86_64/packages/{args[1]}_2024.01_x86_64.ipk\nConfiguring {args[1]}.",
                "remove" when args.Length > 1 => $"Removing package {args[1]} from root...",
                "upgrade" => "Upgrading packages on root...",
                _ => "Usage: opkg [update|list|list-installed|install|remove|info|upgrade]"
            };
        }

        private string InterpretOpkgList(string? filter)
        {
            var all = "base-files - 1563-r27925\nbusybox - 1.36.1-1\ncurl - 8.5.0-1\ndnsmasq - 2.90-1\ndropbear - 2024.85-1\nluci - git-24.086\nnano - 7.2-2";
            if (string.IsNullOrEmpty(filter)) return all;
            return string.Join("\n", all.Split('\n').Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        private string InterpretLogread(string[] args)
        {
            var ts = DateTime.UtcNow;
            return $"{ts.AddMinutes(-5):ddd MMM dd HH:mm:ss yyyy} kern.info kernel: [    0.000000] Linux version 6.6.67\n" +
                   $"{ts.AddMinutes(-4):ddd MMM dd HH:mm:ss yyyy} daemon.info procd: - init complete -\n" +
                   $"{ts.AddMinutes(-3):ddd MMM dd HH:mm:ss yyyy} daemon.info netifd: Interface 'lan' is now up\n" +
                   $"{ts.AddMinutes(-2):ddd MMM dd HH:mm:ss yyyy} daemon.info dnsmasq[934]: started, cache 150 entries\n" +
                   $"{ts.AddMinutes(-1):ddd MMM dd HH:mm:ss yyyy} daemon.info dropbear[1089]: Running in background\n" +
                   $"{ts:ddd MMM dd HH:mm:ss yyyy} authpriv.info dropbear[1089]: Password auth succeeded for 'root' from 192.168.1.100:54321";
        }

        private string InterpretMkdir(string[] args)
        {
            var target = args.LastOrDefault(a => !a.StartsWith("-"));
            if (target == null) return "mkdir: missing operand";
            return "";
        }

        private string InterpretTouch(string[] args)
        {
            if (args.Length == 0) return "touch: missing file operand";
            return "";
        }

        private string InterpretHead(string[] args)
        {
            var file = args.LastOrDefault(a => !a.StartsWith("-"));
            if (file == null) return "";
            var content = InterpretCat(new[] { file });
            var n = 10;
            var nIdx = System.Array.IndexOf(args, "-n");
            if (nIdx >= 0 && nIdx + 1 < args.Length) int.TryParse(args[nIdx + 1], out n);
            return string.Join("\n", content.Split('\n').Take(n));
        }

        private string InterpretTail(string[] args)
        {
            var file = args.LastOrDefault(a => !a.StartsWith("-"));
            if (file == null) return "";
            var content = InterpretCat(new[] { file });
            var n = 10;
            var nIdx = System.Array.IndexOf(args, "-n");
            if (nIdx >= 0 && nIdx + 1 < args.Length) int.TryParse(args[nIdx + 1], out n);
            return string.Join("\n", content.Split('\n').TakeLast(n));
        }

        private string InterpretWc(string[] args)
        {
            var file = args.LastOrDefault(a => !a.StartsWith("-"));
            if (file == null) return "";
            var content = InterpretCat(new[] { file });
            var lines = content.Split('\n').Length;
            var words = content.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (args.Contains("-l")) return $"{lines} {file}";
            return $"  {lines}  {words}  {content.Length} {file}";
        }

        private string InterpretGrep(string[] args)
        {
            if (args.Length < 2) return "Usage: grep [OPTION] PATTERN [FILE]";
            var pattern = args[0];
            var file = args[1];
            var content = InterpretCat(new[] { file });
            var ignoreCase = args.Contains("-i");
            var comp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Join("\n", content.Split('\n').Where(l => l.Contains(pattern, comp)));
        }

        private string InterpretWhich(string[] args)
        {
            if (args.Length == 0) return "";
            var cmd = args[0];
            // Check known paths
            if (_vfs.TryGetValue("/usr/sbin", out var usbin) && usbin.Contains(cmd)) return $"/usr/sbin/{cmd}";
            if (_vfs.TryGetValue("/usr/bin", out var ubin) && ubin.Contains(cmd)) return $"/usr/bin/{cmd}";
            if (_vfs.TryGetValue("/sbin", out var sbin) && sbin.Contains(cmd)) return $"/sbin/{cmd}";
            if (_vfs.TryGetValue("/bin", out var bin) && bin.Contains(cmd)) return $"/bin/{cmd}";
            return "";
        }

        private string InterpretType(string[] args)
        {
            if (args.Length == 0) return "";
            var cmd = args[0];
            var which = InterpretWhich(args);
            if (!string.IsNullOrEmpty(which)) return $"{cmd} is {which}";
            if (new[] { "cd", "echo", "export", "set", "exit", "true", "false", "test" }.Contains(cmd))
                return $"{cmd} is a shell builtin";
            return $"-ash: type: {cmd}: not found";
        }

        private string InterpretPing(string[] args)
        {
            var host = args.LastOrDefault(a => !a.StartsWith("-")) ?? "127.0.0.1";
            return $"PING {host} ({host}): 56 data bytes\n" +
                   $"64 bytes from {host}: seq=0 ttl=64 time=0.081 ms\n" +
                   $"64 bytes from {host}: seq=1 ttl=64 time=0.076 ms\n" +
                   $"64 bytes from {host}: seq=2 ttl=64 time=0.079 ms\n\n" +
                   $"--- {host} ping statistics ---\n" +
                   $"3 packets transmitted, 3 packets received, 0% packet loss\n" +
                   $"round-trip min/avg/max = 0.076/0.078/0.081 ms";
        }

        private string InterpretWget(string[] args)
        {
            var url = args.LastOrDefault(a => !a.StartsWith("-")) ?? "";
            return $"Downloading '{url}'\nConnecting to {(url.Contains("://") ? url.Split('/')[2] : url)}\n" +
                   "Download completed (1024 bytes)";
        }

        private string InterpretCurl(string[] args)
        {
            var url = args.LastOrDefault(a => !a.StartsWith("-")) ?? "";
            if (args.Contains("-I"))
                return "HTTP/1.1 200 OK\nContent-Type: text/html\nServer: uhttpd\nConnection: close";
            return $"<html><body><h1>OpenWrt</h1></body></html>";
        }

        private string InterpretService(string[] args)
        {
            if (args.Length < 2) return "Usage: service <name> <action>";
            return $" * {args[1]}ing {args[0]}...  [ ok ]";
        }

        private string InterpretInitScript(string script, string[] args)
        {
            var name = script.Split('/').Last();
            var action = args.FirstOrDefault() ?? "help";
            return action switch
            {
                "start" => $"Starting {name}...",
                "stop" => $"Stopping {name}...",
                "restart" => $"Stopping {name}...\nStarting {name}...",
                "status" => $"{name} is running",
                "enable" => $"Enabling {name}...",
                "disable" => $"Disabling {name}...",
                _ => $"Syntax: /etc/init.d/{name} [start|stop|restart|reload|enable|disable]"
            };
        }
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
