using WasmMvcRuntime.Abstractions.SignalR;

namespace WasmMvcRuntime.App.Hubs;

/// <summary>
/// Chat hub — shared between Client (WASM) and Cepha (Server).
/// Write once, run on both sides.
/// </summary>
public class ChatHub : Hub
{
    public async Task SendMessage(string user, string message)
        => await Clients.All.SendAsync("ReceiveMessage", user, message, DateTime.Now.ToString("HH:mm:ss"));

    public async Task SendToGroup(string groupName, string user, string message)
        => await Clients.Group(groupName).SendAsync("ReceiveMessage", user, message, DateTime.Now.ToString("HH:mm:ss"));

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        await Clients.All.SendAsync("SystemMessage", $"{Context.ConnectionId} joined group '{groupName}'");
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        await Clients.All.SendAsync("SystemMessage", $"{Context.ConnectionId} left group '{groupName}'");
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.All.SendAsync("SystemMessage", $"User {Context.ConnectionId} connected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("SystemMessage", $"User {Context.ConnectionId} disconnected");
        await base.OnDisconnectedAsync(exception);
    }
}
