using NetContainer.Ref;
using NetContainer.Ref.Orchestrator;
using NetContainer.Ref.Distributions;
using NetContainer.Ref.Guest;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Point to the repo assets — MUST be set before AddNetContainerRef (reads at DI construction)
Environment.SetEnvironmentVariable("NETCONTAINER_HOME",
    @"X:\source\netcontainer-runtime\netcontainer-runtime");
Environment.SetEnvironmentVariable("NETCONTAINER_NATIVE_CORE_DIR",
    @"X:\source\netcontainer-runtime\netcontainer-runtime\assets\native-core");

// ── Register NetContainer.Ref (single line)
builder.Services.AddNetContainerRef(opts =>
{
    opts.MaxConcurrentGuests = 3;
    opts.PreferCliDelegation = false; // Use direct QEMU for snapshot support
    opts.DefaultHardwareProfile = new NetContainer.Ref.Hardware.HardwareProfile
    {
        VirtualCpuCores = 2,
        MemoryMb = 512
    };
});

// ── Auto-start OpenWrt guest on app startup
builder.Services.AddHostedService<GuestBootService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseRouting();
app.UseAuthorization();
app.UseWebSockets();
app.MapNetContainerTerminal("/nc-terminal/{guestId}");

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// ── Background service: boots OpenWrt automatically
public class GuestBootService : BackgroundService
{
    private readonly IRefOrchestratorService _orch;
    private readonly ILogger<GuestBootService> _log;

    public GuestBootService(IRefOrchestratorService orch, ILogger<GuestBootService> log)
    {
        _orch = orch;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Small delay so Kestrel binds first
        await Task.Delay(2000, ct);
        _log.LogInformation("🚀 Starting OpenWrt guest via NetContainer.Ref...");

        try
        {
            var ctx = await _orch.StartGuestAsync(
                KnownDistribution.OpenWrt,
                arch: "x86_64",
                configure: opts => opts with
                {
                    TenantId = "demo",
                    Net = "user"
                },
                ct);

            _log.LogInformation("✅ Guest {Id} running — PID {Pid}, SSH :{Ssh}, HTTP :{Http}",
                ctx.Id, ctx.QemuPid, ctx.SshPort, ctx.HttpPort);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "❌ Failed to start guest");
        }
    }
}
