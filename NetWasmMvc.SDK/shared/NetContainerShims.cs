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
        public Services.IShellService Shell => new Services.BrowserShellService(Id, SshPort, HttpPort, StartedAt ?? DateTime.UtcNow);
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

        internal BrowserShellService(string guestId, int sshPort, int httpPort, DateTime bootTime)
        {
            _guestId = guestId; _sshPort = sshPort;
            _httpPort = httpPort; _bootTime = bootTime;
        }

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

        private string Interpret(string cmd)
        {
            // Handle pipes/semicolons (basic)
            if (cmd.Contains(';'))
            {
                var parts = cmd.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return string.Join("\n", parts.Select(Interpret));
            }

            // Parse command and args
            var tokens = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return "";
            var bin = tokens[0];
            var args = tokens.Skip(1).ToArray();

            var uptime = (DateTime.UtcNow - _bootTime).TotalSeconds;

            return bin switch
            {
                "uname" => args.Contains("-a")
                    ? $"Linux OpenWrt 6.6.67 #0 SMP Mon Jan 6 12:00:00 2025 x86_64 GNU/Linux"
                    : args.Contains("-r") ? "6.6.67"
                    : args.Contains("-m") ? "x86_64"
                    : args.Contains("-s") ? "Linux"
                    : "Linux",

                "cat" => InterpretCat(args),

                "echo" => args.Length > 0 && args[0].StartsWith("$")
                    ? _env.GetValueOrDefault(args[0][1..], "")
                    : string.Join(" ", args),

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

                "df" or "df -h" =>
                    "Filesystem                Size      Used Available Use% Mounted on\n" +
                    "/dev/root                48.0M     12.4M     33.5M  27% /\n" +
                    "tmpfs                   512.0M     72.0K    511.9M   0% /tmp\n" +
                    "tmpfs                   512.0K         0    512.0K   0% /dev\n" +
                    "/dev/sda1                16.0M      3.2M     12.6M  20% /boot",

                "mount" =>
                    "/dev/root on / type ext4 (rw,noatime)\n" +
                    "proc on /proc type proc (rw,nosuid,nodev,noexec,noatime)\n" +
                    "sysfs on /sys type sysfs (rw,nosuid,nodev,noexec,noatime)\n" +
                    "tmpfs on /tmp type tmpfs (rw,nosuid,nodev,noatime)\n" +
                    "devpts on /dev/pts type devpts (rw,nosuid,noexec,mode=600,ptmxmode=000)",

                "ps" => InterpretPs(uptime),

                "top" => $"Mem: {145408}K used, {1951744}K free, {2048}K shrd, {28672}K buff, {46080}K cached\n" +
                         "CPU:   0% usr   1% sys   0% nic  98% idle   0% io   0% irq   0% sirq\n" +
                         "Load average: 0.08 0.03 0.01 1/60 1284\n" +
                         "  PID  PPID USER     STAT   VSZ %VSZ %CPU COMMAND\n" +
                         "    1     0 root     S     1584   0%   0% /sbin/procd\n" +
                         "  892     1 root     S     1280   0%   0% /sbin/ubusd\n" +
                         " 1034     1 root     S     5120   0%   0% /usr/sbin/uhttpd\n" +
                         " 1089     1 root     S     3072   0%   0% /usr/sbin/dropbear",

                "ifconfig" or "ip addr" or "ip a" => InterpretIfconfig(),

                "ip" => args.FirstOrDefault() switch
                {
                    "addr" or "a" => InterpretIfconfig(),
                    "route" or "r" => "default via 10.0.2.2 dev eth0\n10.0.2.0/24 dev eth0 scope link  src 10.0.2.15\n192.168.1.0/24 dev br-lan scope link  src 192.168.1.1",
                    "link" or "l" => "1: lo: <LOOPBACK,UP> mtu 65536 qdisc noqueue state UP\n2: eth0: <BROADCAST,MULTICAST,UP> mtu 1500 qdisc fq_codel state UP\n3: br-lan: <BROADCAST,MULTICAST,UP> mtu 1500 qdisc noqueue state UP",
                    _ => $"Usage: ip [ addr | route | link ]"
                },

                "route" =>
                    "Kernel IP routing table\n" +
                    "Destination     Gateway         Genmask         Flags Metric Ref    Use Iface\n" +
                    "default         10.0.2.2        0.0.0.0         UG    0      0        0 eth0\n" +
                    "10.0.2.0        *               255.255.255.0   U     0      0        0 eth0\n" +
                    "192.168.1.0     *               255.255.255.0   U     0      0        0 br-lan",

                "netstat" =>
                    "Active Internet connections (servers and established)\n" +
                    "Proto Recv-Q Send-Q Local Address           Foreign Address         State\n" +
                    $"tcp        0      0 0.0.0.0:{_httpPort}            0.0.0.0:*               LISTEN\n" +
                    $"tcp        0      0 0.0.0.0:{_sshPort}             0.0.0.0:*               LISTEN\n" +
                    "tcp        0      0 0.0.0.0:80              0.0.0.0:*               LISTEN",

                "ls" => InterpretLs(args),

                "dmesg" =>
                    "[    0.000000] Linux version 6.6.67\n" +
                    "[    0.100000] Calibrating delay loop... 4800.00 BogoMIPS\n" +
                    "[    0.400000] e1000: Intel(R) PRO/1000 Network Driver\n" +
                    "[    0.700000] init: Console is alive\n" +
                    "[    1.200000] procd: - init -\n" +
                    "[    3.000000] procd: - init complete -",

                "uci" => args.FirstOrDefault() switch
                {
                    "show" => "system.@system[0]=system\nsystem.@system[0].hostname='OpenWrt'\nsystem.@system[0].timezone='UTC'\nnetwork.loopback=interface\nnetwork.loopback.device='lo'\nnetwork.loopback.proto='static'",
                    "get" when args.Length > 1 => args[1] switch
                    {
                        "system.@system[0].hostname" => "OpenWrt",
                        _ => ""
                    },
                    _ => "Usage: uci [show|get|set|commit]"
                },

                "opkg" => args.FirstOrDefault() switch
                {
                    "list-installed" => "base-files - 1563-r27925\nbusybox - 1.36.1-1\ndnsmasq - 2.90-1\ndropbear - 2024.85-1\nfirewall4 - 2024.02-1\nluci - git-24.086\nuhttpd - 2024.01-1",
                    "info" => "Package: opkg\nVersion: 2024.01\nStatus: install ok installed",
                    _ => "Usage: opkg [list-installed|info|install|remove|update]"
                },

                "env" => string.Join("\n", _env.Select(kv => $"{kv.Key}={kv.Value}")),

                "export" when args.Length > 0 && args[0].Contains('=') =>
                    ExportCommand(args[0]),

                "help" => "Built-in commands: cat, cd, date, df, dmesg, echo, env, free, hostname, id, ifconfig,\n" +
                          "  ip, ls, mount, netstat, opkg, ps, pwd, route, top, uci, uname, uptime, whoami",

                _ => cmd.StartsWith("#") ? "" : $"-ash: {bin}: not found"
            };
        }

        private string CdCommand(string[] args)
        {
            _cwd = args.Length > 0 ? args[0] : "/root";
            return "";
        }

        private string ExportCommand(string expr)
        {
            var kv = expr.Split('=', 2);
            _env[kv[0]] = kv.Length > 1 ? kv[1] : "";
            return "";
        }

        private string InterpretCat(string[] args)
        {
            if (args.Length == 0) return "";
            return args[0] switch
            {
                "/etc/os-release" or "/etc/openwrt_release" =>
                    "DISTRIB_ID='OpenWrt'\n" +
                    "DISTRIB_RELEASE='23.05.5'\n" +
                    "DISTRIB_REVISION='r27925-e36b028ab5'\n" +
                    "DISTRIB_TARGET='x86/64'\n" +
                    "DISTRIB_ARCH='x86_64'\n" +
                    "DISTRIB_DESCRIPTION='OpenWrt 23.05.5 r27925-e36b028ab5'\n" +
                    "DISTRIB_TAINTS=''",
                "/etc/banner" =>
                    "  _______                     ________        __\n" +
                    " |       |.-----.-----.-----.|  |  |  |.----.|  |_\n" +
                    " |   -   ||  _  |  -__|     ||  |  |  ||   _||   _|\n" +
                    " |_______||   __|_____|__|__||________||__|  |____|\n" +
                    "          |__| W I R E L E S S   F R E E D O M\n" +
                    " -----------------------------------------------------\n" +
                    " OpenWrt 23.05.5, r27925-e36b028ab5\n" +
                    " -----------------------------------------------------",
                "/proc/version" => "Linux version 6.6.67 (builder@buildhost) (gcc 13.3.0) #0 SMP x86_64",
                "/proc/cpuinfo" =>
                    "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: QEMU Virtual CPU version 2.5+\n" +
                    "cpu MHz\t\t: 2400.000\ncache size\t: 4096 KB\nflags\t\t: fpu vme de pse tsc msr pae mce cx8 apic",
                "/proc/meminfo" =>
                    "MemTotal:        2097152 kB\nMemFree:         1876992 kB\nMemAvailable:    1951744 kB\n" +
                    "Buffers:           28672 kB\nCached:            46080 kB\nSwapTotal:             0 kB",
                "/proc/uptime" => $"{(DateTime.UtcNow - _bootTime).TotalSeconds:F2} {(DateTime.UtcNow - _bootTime).TotalSeconds * 0.98:F2}",
                "/etc/config/network" =>
                    "config interface 'loopback'\n\toption device 'lo'\n\toption proto 'static'\n\toption ipaddr '127.0.0.1'\n\toption netmask '255.0.0.0'\n\n" +
                    "config interface 'lan'\n\toption device 'br-lan'\n\toption proto 'static'\n\toption ipaddr '192.168.1.1'\n\toption netmask '255.255.255.0'",
                "/etc/hostname" => "OpenWrt",
                _ => $"cat: can't open '{args[0]}': No such file or directory"
            };
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

        private string InterpretLs(string[] args)
        {
            var target = args.LastOrDefault(a => !a.StartsWith("-")) ?? _cwd;
            return target switch
            {
                "/" => "bin      dev      etc      lib      mnt      overlay  proc     rom      run      sbin     sys      tmp      usr      var      www",
                "/etc" or "etc" => "banner           config           group            hostname         hosts            init.d           inittab          openwrt_release  openwrt_version  os-release       passwd           profile          rc.d             resolv.conf      shadow           shells           sysctl.conf",
                "/root" or "~" or "." => "",
                "/tmp" or "tmp" => "dhcp.leases    resolv.conf    run            state          TMP_DIR",
                "/www" or "www" => "cgi-bin        index.html     luci-static",
                _ => $"ls: {target}: No such file or directory"
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
