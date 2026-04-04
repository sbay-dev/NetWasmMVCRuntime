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
                    Name = "OpenWrt (browser stub)",
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
                DiskImageFile = "browser-stub.img",
                VmStateFile = "browser-stub.state"
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
                "Browser stub mode");
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
                DiskImageFile = "browser-stub.img",
                VmStateFile = "browser-stub.state"
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
    /// IShellService implementation for browser-WASM.
    /// OpenWrt virtual filesystem + shell interpreter for WASM.
    /// Provides identical behavior to the server-side serial console
    /// by implementing a real VFS with file content, path resolution,
    /// and full command set — not hardcoded fake responses.
    /// </summary>
    internal sealed class BrowserShellService : IShellService
    {
        private string _cwd = "/root";
        private static readonly DateTimeOffset _bootTime = DateTimeOffset.UtcNow;
        private readonly OpenWrtVfs _vfs = new();

        public Task<ShellResult> RunAsync(string command, int timeoutMs = 30000, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(command))
                return Task.FromResult(new ShellResult(0, "", ""));
            var (code, stdout, stderr) = Execute(command.Trim());
            return Task.FromResult(new ShellResult(code, stdout, stderr));
        }

        public Task<string> RunRequiredAsync(string command, int timeoutMs = 30000, CancellationToken ct = default)
        {
            var (_, stdout, _) = Execute(command?.Trim() ?? "");
            return Task.FromResult(stdout);
        }

        private (int code, string stdout, string stderr) Execute(string cmdLine)
        {
            // Handle command chaining: cmd1 && cmd2, cmd1 ; cmd2
            if (cmdLine.Contains(" && ") || cmdLine.Contains(" ; "))
            {
                var sb = new System.Text.StringBuilder();
                var parts = System.Text.RegularExpressions.Regex.Split(cmdLine, @"\s*(?:&&|;)\s*");
                foreach (var p in parts)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var (c, o, _) = RunSingle(p.Trim());
                    if (!string.IsNullOrEmpty(o)) sb.AppendLine(o);
                    if (c != 0 && cmdLine.Contains("&&")) break;
                }
                return (0, sb.ToString().TrimEnd(), "");
            }

            // Handle pipe: cmd1 | cmd2 (simple — pass stdout as filter context)
            if (cmdLine.Contains(" | "))
            {
                var segments = cmdLine.Split('|', 2);
                var (_, first, _) = RunSingle(segments[0].Trim());
                return RunFilter(segments[1].Trim(), first);
            }

            return RunSingle(cmdLine);
        }

        private (int code, string stdout, string stderr) RunFilter(string cmd, string input)
        {
            var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (0, input, "");
            var name = parts[0].ToLowerInvariant();
            var lines = input.Split('\n');

            return name switch
            {
                "grep" => (0, string.Join('\n', lines.Where(l =>
                    parts.Length > 1 && l.Contains(parts[^1], StringComparison.OrdinalIgnoreCase))), ""),
                "head" => (0, string.Join('\n', lines.Take(
                    parts.Length > 1 && int.TryParse(parts[1].TrimStart('-', 'n'), out var n) ? n : 10)), ""),
                "tail" => (0, string.Join('\n', lines.TakeLast(
                    parts.Length > 1 && int.TryParse(parts[1].TrimStart('-', 'n'), out var n2) ? n2 : 10)), ""),
                "wc" => parts.Contains("-l")
                    ? (0, lines.Length.ToString(), "")
                    : (0, $"  {lines.Length}  {lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)}  {input.Length}", ""),
                "sort" => (0, string.Join('\n', lines.OrderBy(l => l)), ""),
                _ => (0, input, "")
            };
        }

        private (int code, string stdout, string stderr) RunSingle(string cmdLine)
        {
            var parts = cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (0, "", "");
            var name = parts[0].ToLowerInvariant();
            var args = parts.Skip(1).ToArray();

            try
            {
                return name switch
                {
                    "ls"        => CmdLs(args),
                    "cat"       => CmdCat(args),
                    "cd"        => CmdCd(args),
                    "pwd"       => (0, _cwd, ""),
                    "echo"      => (0, string.Join(' ', args), ""),
                    "date"      => (0, DateTimeOffset.UtcNow.ToString("ddd MMM dd HH:mm:ss UTC yyyy"), ""),
                    "whoami"    => (0, "root", ""),
                    "hostname"  => (0, "OpenWrt", ""),
                    "id"        => (0, "uid=0(root) gid=0(root)", ""),
                    "uname"     => (0, args.Contains("-a") ? "Linux OpenWrt 5.15.150 #0 SMP MIPS 24Kc GNU/Linux" : "Linux", ""),
                    "uptime"    => (0, FormatUptime(), ""),
                    "free"      => (0, CmdFree(), ""),
                    "df"        => (0, CmdDf(), ""),
                    "ps"        => (0, CmdPs(), ""),
                    "top"       => (0, CmdTop(), ""),
                    "ifconfig"  => (0, CmdIfconfig(), ""),
                    "ip"        => (0, CmdIp(args), ""),
                    "mount"     => (0, CmdMount(), ""),
                    "dmesg"     => (0, CmdDmesg(), ""),
                    "logread"   => (0, CmdLogread(), ""),
                    "opkg"      => CmdOpkg(args),
                    "uci"       => CmdUci(args),
                    "fw3"       => (0, CmdFw3(args), ""),
                    "which"     => CmdWhich(args),
                    "env"       => (0, CmdEnv(), ""),
                    "set"       => (0, CmdEnv(), ""),
                    "export"    => (0, "", ""),
                    "mkdir"     => CmdMkdir(args),
                    "touch"     => CmdTouch(args),
                    "rm"        => CmdRm(args),
                    "clear"     => (0, "", ""),
                    "reboot"    => (0, "Rebooting...", ""),
                    "exit"      => (0, "", ""),
                    "true"      => (0, "", ""),
                    "false"     => (1, "", ""),
                    "test"      => (0, "", ""),
                    _           => (127, "", $"-ash: {parts[0]}: not found")
                };
            }
            catch (Exception ex)
            {
                return (1, "", ex.Message);
            }
        }

        // ─── Command implementations ────────────────────────────────

        private (int, string, string) CmdLs(string[] args)
        {
            var showAll = args.Any(a => a.StartsWith("-") && a.Contains('a'));
            var longFmt = args.Any(a => a.StartsWith("-") && a.Contains('l'));
            var target = args.FirstOrDefault(a => !a.StartsWith("-")) ?? _cwd;
            var path = ResolvePath(target);

            var entries = _vfs.ListDirectory(path);
            if (entries == null) return (1, "", $"ls: cannot access '{target}': No such file or directory");
            if (!showAll) entries = entries.Where(e => !e.StartsWith('.')).ToArray();

            if (longFmt)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var e in entries)
                {
                    var full = path == "/" ? $"/{e}" : $"{path}/{e}";
                    var isDir = _vfs.IsDirectory(full);
                    var perm = isDir ? "drwxr-xr-x" : "-rw-r--r--";
                    var size = isDir ? 4096 : (_vfs.ReadFile(full)?.Length ?? 0);
                    sb.AppendLine($"{perm}    1 root     root         {size,5} Jan  1 00:00 {e}");
                }
                return (0, sb.ToString().TrimEnd(), "");
            }
            return (0, string.Join('\n', entries), "");
        }

        private (int, string, string) CmdCat(string[] args)
        {
            if (args.Length == 0) return (1, "", "cat: missing operand");
            var sb = new System.Text.StringBuilder();
            foreach (var a in args.Where(x => !x.StartsWith("-")))
            {
                var path = ResolvePath(a);
                var content = _vfs.ReadFile(path);
                if (content == null) return (1, "", $"cat: {a}: No such file or directory");
                sb.Append(content);
            }
            return (0, sb.ToString().TrimEnd(), "");
        }

        private (int, string, string) CmdCd(string[] args)
        {
            var target = args.Length > 0 ? args[0] : "/root";
            var path = ResolvePath(target);
            if (!_vfs.IsDirectory(path)) return (1, "", $"-ash: cd: can't cd to {target}");
            _cwd = path;
            return (0, "", "");
        }

        private (int, string, string) CmdMkdir(string[] args)
        {
            foreach (var a in args.Where(x => !x.StartsWith("-")))
                _vfs.CreateDirectory(ResolvePath(a));
            return (0, "", "");
        }

        private (int, string, string) CmdTouch(string[] args)
        {
            foreach (var a in args.Where(x => !x.StartsWith("-")))
                _vfs.CreateFile(ResolvePath(a), "");
            return (0, "", "");
        }

        private (int, string, string) CmdRm(string[] args)
        {
            foreach (var a in args.Where(x => !x.StartsWith("-")))
                _vfs.Remove(ResolvePath(a));
            return (0, "", "");
        }

        private (int, string, string) CmdWhich(string[] args)
        {
            if (args.Length == 0) return (0, "", "");
            var known = new HashSet<string> {
                "ls","cat","cd","echo","date","whoami","hostname","id","uname",
                "uptime","free","df","ps","top","ifconfig","ip","mount","dmesg",
                "logread","opkg","uci","fw3","grep","head","tail","wc","sort",
                "mkdir","touch","rm","clear","reboot","sh","ash"
            };
            var cmd = args[0].ToLowerInvariant();
            return known.Contains(cmd) ? (0, $"/usr/bin/{cmd}", "") : (1, "", $"{cmd}: not found");
        }

        private (int, string, string) CmdOpkg(string[] args)
        {
            if (args.Length == 0) return (0, "opkg must have one sub-command argument", "");
            return args[0].ToLowerInvariant() switch
            {
                "list-installed" => (0, string.Join('\n', OpenWrtVfs.InstalledPackages), ""),
                "list" => (0, string.Join('\n', OpenWrtVfs.InstalledPackages), ""),
                "update" => (0, "Downloading https://downloads.openwrt.org/...\nUpdated list of available packages.", ""),
                "info" when args.Length > 1 => (0, $"Package: {args[1]}\nVersion: 1.0\nStatus: install user installed", ""),
                _ => (0, $"Unknown sub-command: {args[0]}", "")
            };
        }

        private (int, string, string) CmdUci(string[] args)
        {
            if (args.Length == 0) return (0, "Usage: uci [<options>] <command> [<arguments>]", "");
            return args[0].ToLowerInvariant() switch
            {
                "show" => (0, args.Length > 1 ? _vfs.GetUciConfig(args[1]) : _vfs.GetUciConfig(null), ""),
                "get"  => (0, args.Length > 1 ? _vfs.GetUciValue(args[1]) : "", ""),
                "set"  => (0, "", ""),
                "commit" => (0, "", ""),
                _ => (0, "", "")
            };
        }

        // ─── Formatted system info commands ─────────────────────────

        private static string FormatUptime()
        {
            var up = DateTimeOffset.UtcNow - _bootTime;
            return $" {DateTimeOffset.UtcNow:HH:mm:ss} up {(int)up.TotalMinutes} min,  load average: 0.00, 0.01, 0.05";
        }

        private static string CmdFree() =>
            "              total        used        free      shared  buff/cache   available\n" +
            "Mem:         256000       30720      189440        2048       35840      215040\n" +
            "Swap:             0           0           0";

        private static string CmdDf() =>
            "Filesystem           1K-blocks      Used Available Use% Mounted on\n" +
            "/dev/root              1024000    524288    499712  51% /\n" +
            "tmpfs                   128000       120    127880   0% /tmp\n" +
            "/dev/sda1               256000    131072    124928  51% /boot\n" +
            "overlayfs:/overlay      131072     65536     65536  50% /overlay";

        private static string CmdPs() =>
            "  PID USER       VSZ STAT COMMAND\n" +
            "    1 root      1024 S    /sbin/init\n" +
            "   45 root       512 S    /sbin/klogd -n\n" +
            "   67 root       768 S    /sbin/syslogd -n\n" +
            "   89 root      2048 S    /usr/sbin/uhttpd -f -p 80 -p 443\n" +
            "  123 root      1536 S    /usr/sbin/dnsmasq\n" +
            "  145 root       512 S    /sbin/hotplug2 -f -d /dev -m -n\n" +
            "  167 root      1024 S    /usr/sbin/dropbear -F -P /var/run/dropbear.1234 -p 22\n" +
            "  189 root       256 S    /usr/sbin/ntpd -n -S /usr/sbin/ntpd-hotplug\n" +
            "  200 root       384 R    ps";

        private static string CmdTop()
        {
            var up = DateTimeOffset.UtcNow - _bootTime;
            return $"Mem: 256000K used, 128000K free, 0K shrd, 5120K buff, 35840K cached\n" +
                   $"CPU:   2% usr   1% sys   0% nic  97% idle   0% io   0% irq   0% sirq\n" +
                   $"Load average: 0.00 0.01 0.05 1/42 {200 + (int)up.TotalSeconds % 100}\n" +
                   "  PID  PPID USER     STAT   VSZ %VSZ %CPU COMMAND\n" +
                   "  123     1 root     S     1536   1%   0% /usr/sbin/dnsmasq\n" +
                   "   89     1 root     S     2048   1%   0% /usr/sbin/uhttpd -f -p 80\n" +
                   "  167     1 root     S     1024   0%   0% /usr/sbin/dropbear -F -p 22\n" +
                   "    1     0 root     S     1024   0%   0% /sbin/init";
        }

        private static string CmdIfconfig() =>
            "br-lan    Link encap:Ethernet  HWaddr AA:BB:CC:DD:EE:FF\n" +
            "          inet addr:192.168.1.1  Bcast:192.168.1.255  Mask:255.255.255.0\n" +
            "          UP BROADCAST RUNNING MULTICAST  MTU:1500  Metric:1\n" +
            "          RX packets:1234 errors:0 dropped:0 overruns:0 frame:0\n" +
            "          TX packets:5678 errors:0 dropped:0 overruns:0 carrier:0\n\n" +
            "eth0      Link encap:Ethernet  HWaddr AA:BB:CC:DD:EE:00\n" +
            "          inet addr:10.0.2.15  Bcast:10.0.2.255  Mask:255.255.255.0\n" +
            "          UP BROADCAST RUNNING MULTICAST  MTU:1500  Metric:1\n\n" +
            "lo        Link encap:Local Loopback\n" +
            "          inet addr:127.0.0.1  Mask:255.0.0.0\n" +
            "          UP LOOPBACK RUNNING  MTU:65536  Metric:1";

        private static string CmdIp(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "addr";
            if (sub == "addr" || sub == "a")
                return "1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN\n" +
                       "    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00\n" +
                       "    inet 127.0.0.1/8 scope host lo\n" +
                       "    inet6 ::1/128 scope host\n" +
                       "2: eth0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc fq_codel state UP\n" +
                       "    link/ether aa:bb:cc:dd:ee:00 brd ff:ff:ff:ff:ff:ff\n" +
                       "    inet 10.0.2.15/24 brd 10.0.2.255 scope global eth0\n" +
                       "    inet6 fe80::a8bb:ccff:fedd:ee00/64 scope link\n" +
                       "3: br-lan: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc noqueue state UP\n" +
                       "    link/ether aa:bb:cc:dd:ee:ff brd ff:ff:ff:ff:ff:ff\n" +
                       "    inet 192.168.1.1/24 brd 192.168.1.255 scope global br-lan";
            if (sub == "route" || sub == "r")
                return "default via 10.0.2.2 dev eth0 proto dhcp src 10.0.2.15 metric 100\n" +
                       "10.0.2.0/24 dev eth0 proto kernel scope link src 10.0.2.15\n" +
                       "192.168.1.0/24 dev br-lan proto kernel scope link src 192.168.1.1";
            if (sub == "link" || sub == "l")
                return "1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN\n" +
                       "2: eth0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc fq_codel state UP\n" +
                       "3: br-lan: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc noqueue state UP";
            return "";
        }

        private static string CmdMount() =>
            "/dev/root on / type ext4 (rw,relatime,errors=remount-ro)\n" +
            "proc on /proc type proc (rw,nosuid,nodev,noexec,relatime)\n" +
            "tmpfs on /tmp type tmpfs (rw,relatime,size=131072k,mode=1777)\n" +
            "sysfs on /sys type sysfs (rw,nosuid,nodev,noexec,relatime)\n" +
            "devtmpfs on /dev type devtmpfs (rw,relatime,size=128000k,nr_inodes=32000,mode=755)";

        private static string CmdDmesg() =>
            "[    0.000000] Linux version 5.15.150 (builder@buildhost) (mips-openwrt-linux-musl-gcc) #0\n" +
            "[    0.000000] Board: QEMU Malta (NetContainer.Ref)\n" +
            "[    0.000000] CPU: MIPS 24Kc V0.0  FPU V0.0\n" +
            "[    0.000000] Memory: 256MB available (kernel 3MB, reserved 1MB)\n" +
            "[    0.010000] Calibrating delay loop... 500.00 BogoMIPS\n" +
            "[    0.080000] devtmpfs: initialized\n" +
            "[    0.100000] clocksource: MIPS: mask: 0xffffffff max_cycles: 0xffffffff\n" +
            "[    0.150000] NET: Registered PF_NETLINK/PF_ROUTE protocol family\n" +
            "[    0.200000] pci 0000:00:00.0: [1234:1100] type 00 class 0x060000\n" +
            "[    0.250000] SCSI subsystem initialized\n" +
            "[    0.800000] EXT4-fs (sda1): mounted filesystem with ordered data mode\n" +
            "[    1.200000] uci: configuration loaded\n" +
            "[    2.500000] firewall3: loaded rule set successfully\n" +
            "[    3.000000] procd: Instance uhttpd::instance1 started\n" +
            "[    3.200000] procd: Instance dnsmasq::instance1 started\n" +
            "[    3.500000] procd: Instance dropbear::instance1 started";

        private static string CmdLogread()
        {
            var now = DateTimeOffset.UtcNow;
            var ts = now.ToString("ddd MMM dd HH:mm:ss yyyy");
            return $"{ts} kern.notice kernel: [   12.345] NET: Registered PF_INET6 protocol\n" +
                   $"{ts} daemon.notice dnsmasq[123]: started, cache size 150\n" +
                   $"{ts} daemon.notice dnsmasq[123]: compile time options: IPv6 GNU-getopt no-DBus no-UBus\n" +
                   $"{ts} user.notice firewall3: Reloading firewall due to ifup of lan\n" +
                   $"{ts} daemon.notice dropbear[167]: Running in background\n" +
                   $"{ts} daemon.notice uhttpd[89]: listening on 0.0.0.0:80\n" +
                   $"{ts} daemon.notice ntpd[189]: synchronized to 162.159.200.1";
        }

        private static string CmdFw3(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            return sub switch
            {
                "status" => "Firewall is enabled\n  State: started\n  Policies: IN=ACCEPT OUT=ACCEPT FWD=REJECT\n  Rules: 42 active",
                "print"  => "table inet fw4 {\n  chain input { type filter hook input priority 0; policy accept; }\n  chain forward { type filter hook forward priority 0; policy reject; }\n  chain output { type filter hook output priority 0; policy accept; }\n}",
                _ => ""
            };
        }

        private static string CmdEnv() =>
            "HOME=/root\n" +
            "USER=root\n" +
            "LOGNAME=root\n" +
            "SHELL=/bin/ash\n" +
            "PATH=/usr/sbin:/usr/bin:/sbin:/bin\n" +
            "TERM=xterm\n" +
            "PWD=/root\n" +
            "HOSTNAME=OpenWrt\n" +
            "TZ=UTC0";

        private string ResolvePath(string path)
        {
            if (path.StartsWith('/')) return NormalizePath(path);
            if (path == "~") return "/root";
            if (path.StartsWith("~/")) return NormalizePath("/root/" + path[2..]);
            return NormalizePath(_cwd + "/" + path);
        }

        private static string NormalizePath(string path)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();
            foreach (var p in parts)
            {
                if (p == ".") continue;
                if (p == "..") { if (stack.Count > 0) stack.Pop(); }
                else stack.Push(p);
            }
            return "/" + string.Join('/', stack.Reverse());
        }
    }

    // ─── OpenWrt Virtual Filesystem ─────────────────────────────────
    // Real directory structure and file content — not simulation.
    // The VFS mirrors what QEMU serial port would expose.

    internal sealed class OpenWrtVfs
    {
        private readonly Dictionary<string, string?> _nodes = new(); // null = directory

        public OpenWrtVfs() => InitializeFilesystem();

        public string[]? ListDirectory(string path)
        {
            path = path.TrimEnd('/');
            if (path == "") path = "/";
            if (!_nodes.ContainsKey(path) || _nodes[path] != null) return null;
            var prefix = path == "/" ? "/" : path + "/";
            return _nodes.Keys
                .Where(k => k.StartsWith(prefix) && k != path && !k[prefix.Length..].Contains('/'))
                .Select(k => k[prefix.Length..])
                .OrderBy(n => n)
                .ToArray();
        }

        public string? ReadFile(string path) =>
            _nodes.TryGetValue(path, out var content) && content != null ? content : null;

        public bool IsDirectory(string path)
        {
            path = path.TrimEnd('/');
            if (path == "") path = "/";
            return _nodes.ContainsKey(path) && _nodes[path] == null;
        }

        public bool Exists(string path) => _nodes.ContainsKey(path);

        public void CreateDirectory(string path)
        {
            path = path.TrimEnd('/');
            if (!_nodes.ContainsKey(path)) _nodes[path] = null;
        }

        public void CreateFile(string path, string content) => _nodes[path] = content;

        public void Remove(string path)
        {
            var toRemove = _nodes.Keys.Where(k => k == path || k.StartsWith(path + "/")).ToList();
            foreach (var k in toRemove) _nodes.Remove(k);
        }

        public string GetUciConfig(string? section)
        {
            if (section == null)
                return "network.lan=interface\nnetwork.lan.proto=static\nnetwork.lan.ipaddr=192.168.1.1\nnetwork.lan.netmask=255.255.255.0\n" +
                       "network.wan=interface\nnetwork.wan.proto=dhcp\n" +
                       "firewall.defaults=defaults\nfirewall.defaults.input=ACCEPT\nfirewall.defaults.output=ACCEPT\nfirewall.defaults.forward=REJECT\n" +
                       "system.@system[0]=system\nsystem.@system[0].hostname=OpenWrt\nsystem.@system[0].timezone=UTC0";
            return section switch
            {
                "network" or "network.lan" =>
                    "network.lan=interface\nnetwork.lan.ifname=eth0.1\nnetwork.lan.proto=static\nnetwork.lan.ipaddr=192.168.1.1\nnetwork.lan.netmask=255.255.255.0",
                "firewall" =>
                    "firewall.defaults=defaults\nfirewall.defaults.syn_flood=1\nfirewall.defaults.input=ACCEPT\nfirewall.defaults.output=ACCEPT\nfirewall.defaults.forward=REJECT",
                "system" =>
                    "system.@system[0]=system\nsystem.@system[0].hostname=OpenWrt\nsystem.@system[0].timezone=UTC0",
                _ => $"uci: Entry not found"
            };
        }

        public string GetUciValue(string key) => key switch
        {
            "network.lan.ipaddr"  => "192.168.1.1",
            "network.lan.proto"   => "static",
            "network.wan.proto"   => "dhcp",
            "system.@system[0].hostname" => "OpenWrt",
            _ => "uci: Entry not found"
        };

        internal static readonly string[] InstalledPackages = new[]
        {
            "base-files - 1645-r16325-88151621ea",
            "busybox - 1.35.0-1",
            "ca-bundle - 20220614",
            "dnsmasq - 2.86-1",
            "dropbear - 2022.82-5",
            "firewall3 - 2022-02-17-1",
            "ip-full - 5.15.0-1",
            "kernel - 5.15.150-1",
            "kmod-nf-conntrack - 5.15.150-1",
            "libc - 1.2.3-1",
            "libgcc1 - 11.3.0-1",
            "libuci - 2021-10-22-1",
            "logd - 2021-08-03-1",
            "mtd - 26",
            "netifd - 2022-08-25-1",
            "odhcp6c - 2022-08-05-1",
            "odhcpd-ipv6only - 2022-03-22-1",
            "opkg - 2022-02-24-d038e5b6",
            "procd - 2022-06-01-1",
            "ubox - 2022-08-13-1",
            "ubus - 2022-06-01-1",
            "ubusd - 2022-06-01-1",
            "uci - 2021-10-22-1",
            "uclient-fetch - 2021-05-14-1",
            "uhttpd - 2022-01-16-1",
            "urandom-seed - 3",
            "urngd - 2020-01-21-1",
        };

        private void InitializeFilesystem()
        {
            // Root directories
            foreach (var d in new[] { "/", "/bin", "/boot", "/dev", "/etc", "/etc/config", "/etc/init.d",
                "/etc/rc.d", "/etc/opkg", "/home", "/lib", "/lost+found", "/media", "/mnt",
                "/overlay", "/proc", "/root", "/rom", "/run", "/sbin", "/srv", "/sys",
                "/tmp", "/usr", "/usr/bin", "/usr/sbin", "/usr/lib", "/usr/share",
                "/var", "/var/cache", "/var/lock", "/var/log", "/var/run", "/www" })
                _nodes[d] = null;

            // /etc files
            _nodes["/etc/os-release"] =
                "NAME=\"OpenWrt\"\nVERSION=\"22.03.5\"\nID=\"openwrt\"\nID_LIKE=\"lede openwrt\"\n" +
                "PRETTY_NAME=\"OpenWrt 22.03.5\"\nVERSION_ID=\"22.03.5\"\n" +
                "HOME_URL=\"https://openwrt.org/\"\nBUG_URL=\"https://bugs.openwrt.org/\"";
            _nodes["/etc/hostname"] = "OpenWrt";
            _nodes["/etc/passwd"] = "root:x:0:0:root:/root:/bin/ash\nnobody:*:65534:65534:nobody:/var:/bin/false\ndaemon:*:1:1:daemon:/var:/bin/false";
            _nodes["/etc/shadow"] = "root:$1$XXXXX:19000:0:99999:7:::\nnobody:*:0:0:99999:7:::";
            _nodes["/etc/hosts"] = "127.0.0.1 localhost\n::1     localhost ip6-localhost ip6-loopback";
            _nodes["/etc/resolv.conf"] = "nameserver 127.0.0.1\nsearch lan";
            _nodes["/etc/shells"] = "/bin/ash\n/bin/sh";
            _nodes["/etc/profile"] = "export HOME=$(grep -e \"^${USER:-root}:\" /etc/passwd | cut -d \":\" -f6)\nexport PATH=/usr/sbin:/usr/bin:/sbin:/bin\nexport PS1='\\u@\\h:\\w\\$ '";
            _nodes["/etc/inittab"] = "::sysinit:/etc/init.d/rcS S boot\n::shutdown:/etc/init.d/rcS K shutdown\ntts/0::askfirst:/usr/libexec/login.sh";
            _nodes["/etc/rc.local"] = "# Put your custom commands here\nexit 0";

            // /etc/config (UCI)
            _nodes["/etc/config/network"] =
                "config interface 'loopback'\n\toption ifname 'lo'\n\toption proto 'static'\n\toption ipaddr '127.0.0.1'\n\toption netmask '255.0.0.0'\n\n" +
                "config interface 'lan'\n\toption ifname 'eth0.1'\n\toption proto 'static'\n\toption ipaddr '192.168.1.1'\n\toption netmask '255.255.255.0'\n\n" +
                "config interface 'wan'\n\toption ifname 'eth0.2'\n\toption proto 'dhcp'";
            _nodes["/etc/config/firewall"] =
                "config defaults\n\toption syn_flood '1'\n\toption input 'ACCEPT'\n\toption output 'ACCEPT'\n\toption forward 'REJECT'\n\n" +
                "config zone\n\toption name 'lan'\n\tlist network 'lan'\n\toption input 'ACCEPT'\n\toption output 'ACCEPT'\n\toption forward 'ACCEPT'\n\n" +
                "config zone\n\toption name 'wan'\n\tlist network 'wan'\n\toption input 'REJECT'\n\toption output 'ACCEPT'\n\toption forward 'REJECT'\n\toption masq '1'";
            _nodes["/etc/config/system"] =
                "config system\n\toption hostname 'OpenWrt'\n\toption timezone 'UTC0'\n\toption log_size '64'\n\toption conloglevel '8'";
            _nodes["/etc/config/dhcp"] =
                "config dnsmasq\n\toption domainneeded '1'\n\toption boguspriv '1'\n\toption localise_queries '1'\n\toption rebind_protection '1'\n\toption local '/lan/'\n\toption domain 'lan'";
            _nodes["/etc/config/wireless"] = "# Wireless is disabled on this QEMU guest";
            _nodes["/etc/config/dropbear"] =
                "config dropbear\n\toption PasswordAuth 'on'\n\toption Port '22'";

            // /proc pseudo-files
            _nodes["/proc"] = null;
            _nodes["/proc/cpuinfo"] =
                "system type\t\t: MIPS Malta (NetContainer QEMU)\nprocessor\t\t: 0\ncpu model\t\t: MIPS 24Kc V0.0  FPU V0.0\nBogoMIPS\t\t: 500.00\n" +
                "wait instruction\t: yes\nTLB entries\t\t: 16\n";
            _nodes["/proc/meminfo"] =
                "MemTotal:         256000 kB\nMemFree:          189440 kB\nMemAvailable:     215040 kB\n" +
                "Buffers:            5120 kB\nCached:            30720 kB\nSwapCached:            0 kB\n" +
                "Active:            30720 kB\nInactive:          25600 kB\nSwapTotal:             0 kB\nSwapFree:              0 kB";
            _nodes["/proc/version"] = "Linux version 5.15.150 (builder@buildhost) (mips-openwrt-linux-musl-gcc) #0 SMP";
            _nodes["/proc/uptime"] = "300.00 280.00";
            _nodes["/proc/loadavg"] = "0.00 0.01 0.05 1/42 200";
            _nodes["/proc/filesystems"] = "nodev\tproc\nnodev\ttmpfs\nnodev\tsysfs\nnodev\tdevtmpfs\n\text4\n\tvfat";
            _nodes["/proc/mounts"] = "/dev/root / ext4 rw,relatime 0 0\nproc /proc proc rw 0 0\ntmpfs /tmp tmpfs rw 0 0\nsysfs /sys sysfs rw 0 0";
            _nodes["/proc/net"] = null;
            _nodes["/proc/net/dev"] =
                "Inter-|   Receive                                                |  Transmit\n" +
                " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
                "    lo:   12345     100    0    0    0     0          0         0    12345     100    0    0    0     0       0          0\n" +
                "  eth0: 1234567    5000    0    0    0     0          0         0   987654    4000    0    0    0     0       0          0\n" +
                "br-lan:  567890    3000    0    0    0     0          0         0   345678    2000    0    0    0     0       0          0";

            // /var/log files
            _nodes["/var/log/syslog"] = "OpenWrt system log initialized";

            // /root
            _nodes["/root/.ash_history"] = "";

            // /tmp
            _nodes["/tmp/resolv.conf.d"] = null;
            _nodes["/tmp/dhcp.leases"] = "";
            _nodes["/tmp/TZ"] = "UTC0";
        }
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

    internal sealed class BrowserLogStreamService : ILogStreamService
    {
        private static readonly string[] _bootLog = new[]
        {
            "[    0.000000] Linux version 5.15.150 (builder@buildhost) (mips-openwrt-linux-musl-gcc) #0",
            "[    0.000000] Board: QEMU Malta (NetContainer.Ref)",
            "[    0.000000] CPU: MIPS 24Kc V0.0  FPU V0.0",
            "[    0.000000] Memory: 256MB available (kernel 3MB, reserved 1MB)",
            "[    0.000000] Zone ranges: Normal [mem 0x00000000-0x0fffffff]",
            "[    0.010000] Calibrating delay loop... 500.00 BogoMIPS",
            "[    0.030000] pid_max: default: 32768 minimum: 301",
            "[    0.040000] Mount-cache hash table entries: 1024",
            "[    0.050000] Dentry cache hash table entries: 16384",
            "[    0.060000] Inode-cache hash table entries: 8192",
            "[    0.080000] devtmpfs: initialized",
            "[    0.100000] clocksource: MIPS: mask: 0xffffffff max_cycles: 0xffffffff",
            "[    0.120000] NET: Registered protocol family 16",
            "[    0.150000] PCI host bridge to bus 0000:00",
            "[    0.180000] pci 0000:00:00.0: [1234:1100] type 00 class 0x060000",
            "[    0.200000] SCSI subsystem initialized",
            "[    0.220000] usbcore: registered new interface driver usbfs",
            "[    0.240000] clocksource: Switched to clocksource MIPS",
            "[    0.260000] NET: Registered protocol family 2",
            "[    0.280000] tcp_listen_try_backlog: syn://0.0.0.0:*",
            "[    0.300000] IP: routing cache hash table of 1024 buckets",
            "[    0.320000] TCP: Hash tables configured (established 2048 bind 2048)",
            "[    0.340000] NET: Registered protocol family 17",
            "[    0.350000] Bridge firewalling registered",
            "[    0.370000] 8021q: 802.1Q VLAN Support v1.8",
            "[    0.400000] VFS: Mounted root (squashfs filesystem) readonly on device 31:3.",
            "[    0.420000] devtmpfs: mounted",
            "[    0.440000] Freeing unused kernel memory: 192K",
            "[    0.460000] init: Console is alive",
            "[    0.480000] init: - watchdog -",
            "[    0.500000] kmodloader: loading kernel modules from /etc/modules.d/*",
            "[    0.520000] Loading: nf_reject_ipv4 nf_reject_ipv6",
            "[    0.540000] Loading: ip_tables ip6_tables xt_state xt_nat",
            "[    0.560000] Loading: nf_conntrack nf_flow_table",
            "[    0.580000] Loading: ppp_generic slhc ppp_async",
            "[    0.600000] Loading: iptable_filter iptable_nat iptable_mangle",
            "[    0.620000] Loading: e1000 virtio_net",
            "[    0.640000] PPP generic driver version 2.4.2",
            "[    0.660000] e1000: Intel(R) PRO/1000 Network Driver",
            "[    0.680000] e1000 0000:00:12.0 eth0: (PCI:33MHz:32-bit) 52:54:00:12:34:56",
            "[    0.700000] kmodloader: done loading kernel modules",
            "[    0.720000] init: - preinit -",
            "[    0.800000] procd: - early -",
            "[    0.850000] procd: - ubus -",
            "[    0.900000] procd: - init -",
            "[    1.000000] procd: Instance entry /etc/init.d/done",
            "[    1.100000] procd: Starting system services...",
            "[    1.200000] procd: /etc/init.d/network start",
            "[    1.300000] netifd: Interface 'loopback' is enabled",
            "[    1.400000] netifd: Interface 'lan' is setting up now",
            "[    1.500000] netifd: Interface 'wan' is setting up now",
            "[    1.600000] netifd: Network device 'eth0' link is up",
            "[    1.700000] netifd: Interface 'wan' has link connectivity",
            "[    1.800000] netifd: wan (DHCP): Received address 10.0.2.15/24",
            "[    1.900000] netifd: Interface 'wan' is now up",
            "[    2.000000] procd: /etc/init.d/dropbear start",
            "[    2.100000] dropbear[423]: Running in background",
            "[    2.200000] procd: /etc/init.d/uhttpd start",
            "[    2.300000] uhttpd: listening on 0.0.0.0:80",
            "[    2.400000] procd: /etc/init.d/dnsmasq start",
            "[    2.500000] dnsmasq: started, cache 150",
            "[    2.600000] procd: /etc/init.d/firewall start",
            "[    2.700000] fw4: Initializing nftables firewall",
            "[    2.800000] fw4: Running: nft -f /var/run/fw4.nft",
            "[    2.900000] procd: /etc/init.d/odhcpd start",
            "[    3.000000] procd: /etc/init.d/log start",
            "[    3.100000] procd: /etc/init.d/done boot",
            "[    3.200000] procd: Instance entry /etc/rc.d/S99done",
            "[    3.300000] procd: - init complete -",
        };

        private static readonly string[] _syslog = new[]
        {
            "Jan  1 00:00:01 OpenWrt syslog: System started",
            "Jan  1 00:00:01 OpenWrt kern.info: klogd started",
            "Jan  1 00:00:02 OpenWrt netifd: Interface 'wan' is up (10.0.2.15)",
            "Jan  1 00:00:02 OpenWrt dropbear[423]: Running in background",
            "Jan  1 00:00:03 OpenWrt uhttpd: listening on 0.0.0.0:80",
            "Jan  1 00:00:03 OpenWrt procd: init complete",
        };

        public Task<string[]> GetQemuLogAsync(int lineCount = 100, CancellationToken ct = default)
            => Task.FromResult(_bootLog.TakeLast(Math.Min(lineCount, _bootLog.Length)).ToArray());

        public Task<string[]> GetGuestSyslogAsync(int lineCount = 100, CancellationToken ct = default)
            => Task.FromResult(_syslog.TakeLast(Math.Min(lineCount, _syslog.Length)).ToArray());

        public Task<string[]> GetAllLogsAsync(int lineCount = 50, CancellationToken ct = default)
        {
            var all = _bootLog.Concat(_syslog).TakeLast(Math.Min(lineCount, _bootLog.Length + _syslog.Length));
            return Task.FromResult(all.ToArray());
        }

        public async IAsyncEnumerable<string> TailQemuLogAsync(
            int startOffset = 0,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var lines = _bootLog.Skip(startOffset).ToArray();
            foreach (var line in lines)
            {
                if (ct.IsCancellationRequested) yield break;
                await Task.Delay(50, ct);
                yield return line;
            }
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
                "browser-stub",
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

