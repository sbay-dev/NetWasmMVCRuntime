namespace WasmMvcRuntime.Abstractions.SignalR;

/// <summary>
/// Base class for SignalR Hubs running in WebAssembly.
/// Mirrors ASP.NET Core's Hub class.
/// </summary>
public abstract class Hub : IDisposable
{
    /// <summary>
    /// Gets the clients connected to the hub
    /// </summary>
    public IHubCallerClients Clients { get; set; } = null!;

    /// <summary>
    /// Gets the group manager
    /// </summary>
    public IGroupManager Groups { get; set; } = null!;

    /// <summary>
    /// Gets the hub caller context
    /// </summary>
    public HubCallerContext Context { get; set; } = null!;

    /// <summary>
    /// Called when a new connection is established
    /// </summary>
    public virtual Task OnConnectedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called when a connection is terminated
    /// </summary>
    public virtual Task OnDisconnectedAsync(Exception? exception) => Task.CompletedTask;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a proxy for invoking methods on connected clients
/// </summary>
public interface IClientProxy
{
    Task SendAsync(string method, params object?[] args);
}

/// <summary>
/// Provides access to caller clients
/// </summary>
public interface IHubCallerClients
{
    /// <summary>All connected clients</summary>
    IClientProxy All { get; }

    /// <summary>The calling client only</summary>
    IClientProxy Caller { get; }

    /// <summary>All clients except the caller</summary>
    IClientProxy Others { get; }

    /// <summary>A specific client by connectionId</summary>
    IClientProxy Client(string connectionId);

    /// <summary>Clients in a specific group</summary>
    IClientProxy Group(string groupName);
}

/// <summary>
/// Manages groups of connections
/// </summary>
public interface IGroupManager
{
    Task AddToGroupAsync(string connectionId, string groupName);
    Task RemoveFromGroupAsync(string connectionId, string groupName);
}

/// <summary>
/// Context for the current hub caller
/// </summary>
public class HubCallerContext
{
    public string ConnectionId { get; set; } = string.Empty;
    public string? UserIdentifier { get; set; }
    public IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
}
