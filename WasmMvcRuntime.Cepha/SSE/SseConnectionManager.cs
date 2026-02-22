using System.Collections.Concurrent;
using System.Text.Json;

namespace WasmMvcRuntime.Cepha.SSE;

/// <summary>
/// Manages active SSE (Server-Sent Events) connections.
/// Each connection is identified by a unique connectionId assigned by the JS host.
/// The manager tracks subscriptions (which controller/path each client is streaming from)
/// and provides broadcast / targeted send capabilities.
/// </summary>
public class SseConnectionManager
{
    /// <summary>
    /// Active connections: connectionId ? metadata
    /// </summary>
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new();

    /// <summary>
    /// Channel subscriptions: channelName ? set of connectionIds
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _channels = new();

    /// <summary>
    /// Event raised when a connection is registered.
    /// </summary>
    public event Action<SseConnection>? OnConnected;

    /// <summary>
    /// Event raised when a connection is removed.
    /// </summary>
    public event Action<string>? OnDisconnected;

    // ??? Connection lifecycle ????????????????????????????????

    /// <summary>
    /// Registers a new SSE connection.
    /// </summary>
    public SseConnection Connect(string connectionId, string path)
    {
        var connection = new SseConnection
        {
            ConnectionId = connectionId,
            Path = path,
            ConnectedAt = DateTime.UtcNow
        };

        _connections[connectionId] = connection;

        // Auto-subscribe to a channel based on the path
        var channel = NormalizeChannel(path);
        Subscribe(connectionId, channel);

        OnConnected?.Invoke(connection);
        return connection;
    }

    /// <summary>
    /// Removes an SSE connection and all its channel subscriptions.
    /// </summary>
    public void Disconnect(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            // Remove from all channels
            foreach (var channel in _channels)
            {
                channel.Value.TryRemove(connectionId, out _);
            }

            OnDisconnected?.Invoke(connectionId);
        }
    }

    // ??? Channel subscriptions ???????????????????????????????

    /// <summary>
    /// Subscribes a connection to a named channel.
    /// </summary>
    public void Subscribe(string connectionId, string channel)
    {
        var subscribers = _channels.GetOrAdd(channel, _ => new ConcurrentDictionary<string, byte>());
        subscribers[connectionId] = 0;

        if (_connections.TryGetValue(connectionId, out var conn))
            conn.Channels.Add(channel);
    }

    /// <summary>
    /// Unsubscribes a connection from a named channel.
    /// </summary>
    public void Unsubscribe(string connectionId, string channel)
    {
        if (_channels.TryGetValue(channel, out var subscribers))
        {
            subscribers.TryRemove(connectionId, out _);
        }

        if (_connections.TryGetValue(connectionId, out var conn))
            conn.Channels.Remove(channel);
    }

    // ??? Send events ?????????????????????????????????????????

    /// <summary>
    /// Sends an SSE event to a specific connection.
    /// </summary>
    public void Send(string connectionId, string eventName, object data)
    {
        if (!_connections.ContainsKey(connectionId)) return;

        var json = data is string s ? s : JsonSerializer.Serialize(data);
        CephaInterop.SseSend(connectionId, eventName, json);
    }

    /// <summary>
    /// Sends an SSE event to all connections subscribed to a channel.
    /// </summary>
    public void SendToChannel(string channel, string eventName, object data)
    {
        if (!_channels.TryGetValue(channel, out var subscribers)) return;

        var json = data is string s ? s : JsonSerializer.Serialize(data);
        foreach (var connId in subscribers.Keys)
        {
            if (_connections.ContainsKey(connId))
                CephaInterop.SseSend(connId, eventName, json);
        }
    }

    /// <summary>
    /// Broadcasts an SSE event to ALL connected clients.
    /// </summary>
    public void Broadcast(string eventName, object data)
    {
        var json = data is string s ? s : JsonSerializer.Serialize(data);
        foreach (var connId in _connections.Keys)
        {
            CephaInterop.SseSend(connId, eventName, json);
        }
    }

    /// <summary>
    /// Sends a heartbeat (comment frame) to keep connections alive.
    /// </summary>
    public void SendHeartbeat()
    {
        foreach (var connId in _connections.Keys)
        {
            CephaInterop.SseSend(connId, "heartbeat", "\"ping\"");
        }
    }

    // ??? Queries ?????????????????????????????????????????????

    public int ConnectionCount => _connections.Count;

    public IReadOnlyCollection<string> GetConnectionIds() => _connections.Keys.ToList().AsReadOnly();

    public SseConnection? GetConnection(string connectionId) =>
        _connections.TryGetValue(connectionId, out var conn) ? conn : null;

    public IReadOnlyCollection<string> GetChannelSubscribers(string channel) =>
        _channels.TryGetValue(channel, out var subs)
            ? subs.Keys.ToList().AsReadOnly()
            : Array.Empty<string>().AsReadOnly();

    public IReadOnlyCollection<string> GetChannels() => _channels.Keys.ToList().AsReadOnly();

    // ??? Helpers ?????????????????????????????????????????????

    private static string NormalizeChannel(string path)
    {
        return path.Trim('/').ToLowerInvariant().Replace('/', '.');
    }

    /// <summary>
    /// Removes stale connections that have been idle beyond the timeout.
    /// </summary>
    public int CleanupStaleConnections(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var stale = _connections
            .Where(kvp => kvp.Value.ConnectedAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in stale)
            Disconnect(id);

        return stale.Count;
    }
}

/// <summary>
/// Represents an active SSE client connection.
/// </summary>
public class SseConnection
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public HashSet<string> Channels { get; set; } = new();
}
