// ═══════════════════════════════════════════════════════════════════
// 🧬 NetContainerShims.cs — Browser-WASM type stubs for NetContainer.Ref
//
// Compiled ONLY when:
//   1. The app references the NetContainer.Ref NuGet package
//   2. Building under NetWasmMvc.SDK (browser-wasm target)
//
// The SDK's MSBuild targets automatically:
//   - Detect the PackageReference to NetContainer.Ref
//   - Remove it (incompatible with browser-wasm)
//   - Define HAS_NETCONTAINER_REF
//   - Include this file (matching types)
//
// Design: Honest minimal stubs — no fake data.
// Real functionality provided via CephaKit when connected.
// ═══════════════════════════════════════════════════════════════════

#if HAS_NETCONTAINER_REF

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ─── Core ────────────────────────────────────────────────────────

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
            services.AddSingleton<Orchestrator.IRefOrchestratorService, BrowserRefOrchestrator>();
            return services;
        }
    }

    internal class BrowserRefOrchestrator : Orchestrator.IRefOrchestratorService
    {
        public IReadOnlyList<Guest.IGuestContext> GetRunningGuests()
            => Array.Empty<Guest.IGuestContext>();

        public Guest.IGuestContext? GetGuest(string id) => null;

        public Task<Guest.IGuestContext> StartGuestAsync(
            Distributions.DistributionProfile distribution,
            string arch = "x86_64",
            Func<Guest.RefGuestOptions, Guest.RefGuestOptions>? configure = null,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException(
                $"Distribution '{distribution.Name}' ({arch}) assets not found.\n" +
                "Expected locations:\n" +
                "  embedded:    $NETCONTAINER_HOME/assets/embedded/  OR  AppBase/assets/embedded/  OR  <repo>/assets/embedded/\n" +
                "  native-core: $NETCONTAINER_HOME/assets/native-core/  OR  AppBase/assets/native-core/\n\n" +
                "Remediation:\n" +
                "  1. Run from the repo root where assets/ is present.\n" +
                "  2. Set NETCONTAINER_HOME to a directory containing assets/.\n" +
                "  3. Set NETCONTAINER_EMBEDDED_DIR / NETCONTAINER_NATIVE_CORE_DIR explicitly.");
        }

        public Task<Snapshot.SnapshotExportResult> ExportSnapshotAsync(
            string guestId, string label, CancellationToken ct = default)
            => throw new InvalidOperationException("No guest running — cannot export snapshot");

        public IReadOnlyList<Snapshot.SnapshotInfo> ListSnapshots()
            => Array.Empty<Snapshot.SnapshotInfo>();

        public Task<Guest.IGuestContext> StartFromSnapshotAsync(
            string dir, CancellationToken ct = default)
            => throw new InvalidOperationException("Snapshot restore not available in browser-wasm");
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
        int SshPort { get; }
        int HttpPort { get; }
        int VncPort { get; }
        int VncWsPort { get; }
        int SerialPort { get; }
        DateTime StartedAt { get; }
        Services.ILogStreamService Logs { get; }
        Task FreezeAsync(CancellationToken ct = default);
        Task ResumeAsync(CancellationToken ct = default);
        Task WaitForSerialInitAsync(CancellationToken ct = default);
    }

    public class VirtualizationInfo
    {
        public string? Hypervisor { get; set; }
        public bool NestedVirtAvailable { get; set; }
    }
}

// ─── Orchestrator ────────────────────────────────────────────────

namespace NetContainer.Ref.Orchestrator
{
    public interface IRefOrchestratorService
    {
        IReadOnlyList<Guest.IGuestContext> GetRunningGuests();
        Guest.IGuestContext? GetGuest(string id);
        Task<Guest.IGuestContext> StartGuestAsync(
            Distributions.DistributionProfile distribution,
            string arch = "x86_64",
            Func<Guest.RefGuestOptions, Guest.RefGuestOptions>? configure = null,
            CancellationToken ct = default);
        Task<Snapshot.SnapshotExportResult> ExportSnapshotAsync(
            string guestId, string label, CancellationToken ct = default);
        IReadOnlyList<Snapshot.SnapshotInfo> ListSnapshots();
        Task<Guest.IGuestContext> StartFromSnapshotAsync(
            string dir, CancellationToken ct = default);
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

// ─── Services ────────────────────────────────────────────────────

namespace NetContainer.Ref.Services
{
    public interface ILogStreamService
    {
        IAsyncEnumerable<string> TailQemuLogAsync(int offset = 0, CancellationToken ct = default);
    }

    public interface IShellService
    {
        Task<ShellResult> ExecAsync(string command, CancellationToken ct = default);
    }

    public class ShellResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
    }

    public interface IAnalyticsService { }
    public interface IPackageService { }
    public interface IQemuAuditService { }
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

// ─── Endpoint Extensions (no-op in browser) ─────────────────────

namespace NetContainer.Ref
{
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

namespace NetContainer.Ref.Terminal
{
    public class XtermWebSocketBridge { }
}

namespace NetContainer.Ref.Cli
{
}

#endif
