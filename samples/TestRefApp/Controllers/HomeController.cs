using Microsoft.AspNetCore.Mvc;
using NetContainer.Ref.Orchestrator;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestRefApp.Controllers;

public class HomeController : Controller
{
    private readonly IRefOrchestratorService _orch;

    public HomeController(IRefOrchestratorService orch) => _orch = orch;

    public IActionResult Index() => View();

    // ── API: list running guests
    [HttpGet("api/guests")]
    public IActionResult ListGuests()
    {
        var guests = _orch.GetRunningGuests().Select(g => new
        {
            g.Id, g.TenantId, g.Arch, g.QemuPid, g.IsRunning,
            g.SshPort, g.HttpPort, g.VncPort, g.SerialPort, g.StartedAt
        });
        return Json(guests);
    }

    // ── SSE: stream live boot logs
    [HttpGet("api/guests/{id}/logs/stream")]
    public async Task StreamLogs(string id, CancellationToken ct)
    {
        var ctx = _orch.GetGuest(id);
        if (ctx == null) { Response.StatusCode = 404; return; }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        int offset = 0;
        if (Request.Headers.TryGetValue("Last-Event-ID", out var lastId)
            && int.TryParse(lastId, out var parsed))
            offset = parsed;

        try
        {
            int lineNum = offset;
            await foreach (var line in ctx.Logs.TailQemuLogAsync(offset, ct))
            {
                var json = JsonSerializer.Serialize(line);
                await Response.WriteAsync($"id: {lineNum}\ndata: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                lineNum++;
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── API: freeze guest
    [HttpPost("api/guests/{id}/freeze")]
    public async Task<IActionResult> Freeze(string id, CancellationToken ct)
    {
        var ctx = _orch.GetGuest(id);
        if (ctx == null) return NotFound();
        await ctx.FreezeAsync(ct);
        return Json(new { status = "frozen", guestId = id });
    }

    // ── API: resume guest
    [HttpPost("api/guests/{id}/resume")]
    public async Task<IActionResult> Resume(string id, CancellationToken ct)
    {
        var ctx = _orch.GetGuest(id);
        if (ctx == null) return NotFound();
        await ctx.ResumeAsync(ct);
        return Json(new { status = "resumed", guestId = id });
    }

    // ── API: export snapshot
    [HttpPost("api/guests/{id}/snapshot")]
    public async Task<IActionResult> ExportSnapshot(string id, CancellationToken ct)
    {
        var result = await _orch.ExportSnapshotAsync(id, "TestRefApp snapshot", ct);
        return Json(result);
    }

    // ── API: list snapshots
    [HttpGet("api/snapshots")]
    public IActionResult ListSnapshots()
    {
        var snaps = _orch.ListSnapshots();
        return Json(snaps);
    }

    // ── API: restore from snapshot
    [HttpPost("api/snapshots/{id}/restore")]
    public async Task<IActionResult> RestoreSnapshot(string id, CancellationToken ct)
    {
        var baseDir = Path.Combine(
            Environment.GetEnvironmentVariable("NETCONTAINER_HOME") ?? ".",
            "ref-snapshots");
        var dir = Path.Combine(baseDir, id);
        if (!Directory.Exists(dir)) return NotFound(new { error = $"Snapshot '{id}' not found" });

        var ctx = await _orch.StartFromSnapshotAsync(dir, ct);
        return Json(new { status = "restored", guestId = ctx.Id, ctx.SshPort, ctx.HttpPort });
    }

    // ── API: send command via serial console
    [HttpPost("api/guests/{id}/exec")]
    public async Task<IActionResult> ExecCommand(string id, [FromBody] ExecRequest req, CancellationToken ct)
    {
        var ctx = _orch.GetGuest(id);
        if (ctx == null) return NotFound();
        if (ctx.SerialPort <= 0) return BadRequest(new { error = "No serial port" });

        // Wait for post-boot serial init to release the port
        try { await ctx.WaitForSerialInitAsync(ct).WaitAsync(TimeSpan.FromSeconds(60), ct); }
        catch (TimeoutException) { return StatusCode(503, new { error = "Guest still initializing" }); }

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", ctx.SerialPort, ct);
            var ns = tcp.GetStream();
            ns.ReadTimeout = 5000;
            ns.WriteTimeout = 3000;

            // Drain pending data
            await Task.Delay(200, ct);
            if (ns.DataAvailable)
            {
                var drain = new byte[8192];
                while (ns.DataAvailable) await ns.ReadAsync(drain, ct);
            }

            // Send Ctrl+C to clear any pending input, wait for prompt
            ns.Write("\x03\r\n"u8);
            await Task.Delay(500, ct);
            if (ns.DataAvailable)
            {
                var drain = new byte[8192];
                while (ns.DataAvailable) await ns.ReadAsync(drain, ct);
            }

            // Send a unique marker, then the command, then end marker
            var marker = $"__NC{Environment.TickCount64:X}__";
            var payload = $"echo {marker}\r\n{req.Command}\r\necho {marker}\r\n";
            ns.Write(Encoding.ASCII.GetBytes(payload));

            // Collect output until we see the end marker
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(req.WaitMs, 1000, 30000));
            var buf = new byte[4096];
            bool gotEnd = false;
            while (DateTime.UtcNow < deadline && !gotEnd)
            {
                await Task.Delay(300, ct);
                while (ns.DataAvailable)
                {
                    int n = await ns.ReadAsync(buf, ct);
                    if (n > 0) sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                if (sb.ToString().Split(marker).Length >= 3) gotEnd = true;
            }

            // Extract text between the marker outputs (index 2 = between first and second marker echo)
            var raw = sb.ToString();
            var parts = raw.Split(marker);
            // parts layout: echo cmd | marker | prompt+echo | command output | echo cmd | marker | ...
            var output = parts.Length >= 4
                ? parts[2]
                : parts.Length >= 3 ? parts[1] : raw;

            // Strip ANSI escape codes
            output = Regex.Replace(output, @"\x1B\[[0-9;]*[a-zA-Z]", "");
            output = Regex.Replace(output, @"\x1B\].*?\x07", ""); // OSC sequences

            // Clean up: remove echo of the command and prompt lines
            var lines = output.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrEmpty(l))
                .Where(l => !l.StartsWith("root@"))
                .Where(l => l != req.Command && !l.StartsWith("echo "))
                .ToList();

            return Json(new { output = string.Join('\n', lines) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new TestRefApp.Models.ErrorViewModel
    {
        RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
    });
}

public record ExecRequest(string Command, int WaitMs = 3000);
