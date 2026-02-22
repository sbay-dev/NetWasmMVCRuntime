using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions.SignalR;

namespace WasmMvcRuntime.Core;

/// <summary>
/// Interface for the SignalR engine
/// </summary>
public interface ISignalREngine
{
    /// <summary>Invoke a hub method from a client connection</summary>
    Task<string?> InvokeAsync(string hubName, string method, string connectionId, string? argsJson);

    /// <summary>Connect a client to a hub</summary>
    Task<string> ConnectAsync(string hubName);

    /// <summary>Disconnect a client from a hub</summary>
    Task DisconnectAsync(string hubName, string connectionId);

    /// <summary>Get registered hub names</summary>
    IReadOnlyCollection<string> GetHubNames();
}

/// <summary>
/// SignalR engine that runs hubs in WebAssembly.
/// Dispatches hub events to JavaScript via a callback.
/// </summary>
public class SignalREngine : ISignalREngine
{
    private readonly Dictionary<string, Type> _hubTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// Callback to dispatch events to JavaScript.
    /// Parameters: hubName, method, connectionId (null=all), argsJson
    /// </summary>
    public Action<string, string, string?, string>? OnClientEvent { get; set; }

    public SignalREngine(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
        ScanHubs();
    }

    private void ScanHubs()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var hubTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(Hub).IsAssignableFrom(t));

                foreach (var hubType in hubTypes)
                {
                    var name = hubType.Name.Replace("Hub", "", StringComparison.OrdinalIgnoreCase);
                    _hubTypes[name] = hubType;
                    _connections[name] = new HashSet<string>();
                }
            }
            catch (ReflectionTypeLoadException) { }
        }
    }

    public IReadOnlyCollection<string> GetHubNames() => _hubTypes.Keys.ToList().AsReadOnly();

    public async Task<string> ConnectAsync(string hubName)
    {
        var connectionId = Guid.NewGuid().ToString("N")[..8];

        if (!_connections.ContainsKey(hubName))
            _connections[hubName] = new HashSet<string>();

        _connections[hubName].Add(connectionId);

        // Call OnConnectedAsync
        var hub = CreateHub(hubName, connectionId);
        if (hub != null)
        {
            await hub.OnConnectedAsync();
            hub.Dispose();
        }

        return connectionId;
    }

    public async Task DisconnectAsync(string hubName, string connectionId)
    {
        if (_connections.TryGetValue(hubName, out var conns))
            conns.Remove(connectionId);

        var hub = CreateHub(hubName, connectionId);
        if (hub != null)
        {
            await hub.OnDisconnectedAsync(null);
            hub.Dispose();
        }
    }

    public async Task<string?> InvokeAsync(string hubName, string method, string connectionId, string? argsJson)
    {
        if (!_hubTypes.TryGetValue(hubName, out var hubType))
            return JsonSerializer.Serialize(new { error = $"Hub '{hubName}' not found" });

        var hub = CreateHub(hubName, connectionId);
        if (hub == null)
            return JsonSerializer.Serialize(new { error = "Failed to create hub instance" });

        try
        {
            var methodInfo = hubType.GetMethod(method,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (methodInfo == null || methodInfo.DeclaringType == typeof(Hub) || methodInfo.DeclaringType == typeof(object))
                return JsonSerializer.Serialize(new { error = $"Method '{method}' not found on hub '{hubName}'" });

            // Parse arguments
            var parameters = methodInfo.GetParameters();
            var args = ParseArguments(argsJson, parameters);

            var result = methodInfo.Invoke(hub, args);

            if (result is Task task)
            {
                await task;
                var resultProp = task.GetType().GetProperty("Result");
                if (resultProp != null && resultProp.PropertyType != typeof(void))
                {
                    var val = resultProp.GetValue(task);
                    return val != null ? JsonSerializer.Serialize(val) : null;
                }
                return null;
            }

            return result != null ? JsonSerializer.Serialize(result) : null;
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return JsonSerializer.Serialize(new { error = inner.Message });
        }
        finally
        {
            hub.Dispose();
        }
    }

    private object?[]? ParseArguments(string? argsJson, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0) return null;
        if (string.IsNullOrEmpty(argsJson)) return new object?[parameters.Length];

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var arr = doc.RootElement;

            if (arr.ValueKind == JsonValueKind.Array)
            {
                var args = new object?[parameters.Length];
                for (int i = 0; i < parameters.Length && i < arr.GetArrayLength(); i++)
                {
                    args[i] = JsonSerializer.Deserialize(arr[i].GetRawText(), parameters[i].ParameterType);
                }
                return args;
            }
        }
        catch { }

        return new object?[parameters.Length];
    }

    private Hub? CreateHub(string hubName, string connectionId)
    {
        if (!_hubTypes.TryGetValue(hubName, out var hubType)) return null;

        Hub? hub;
        if (_serviceProvider != null)
        {
            try { hub = (Hub)ActivatorUtilities.CreateInstance(_serviceProvider, hubType); }
            catch { hub = (Hub?)Activator.CreateInstance(hubType); }
        }
        else
        {
            hub = (Hub?)Activator.CreateInstance(hubType);
        }

        if (hub == null) return null;

        var callerClients = new WasmHubCallerClients(hubName, connectionId, _connections, _groups, DispatchToClient);
        hub.Clients = callerClients;
        hub.Groups = new WasmGroupManager(_groups);
        hub.Context = new HubCallerContext { ConnectionId = connectionId };

        return hub;
    }

    private void DispatchToClient(string hubName, string method, string? targetConnectionId, string argsJson)
    {
        OnClientEvent?.Invoke(hubName, method, targetConnectionId, argsJson);
    }
}

/// <summary>
/// WASM implementation of IHubCallerClients
/// </summary>
internal class WasmHubCallerClients : IHubCallerClients
{
    private readonly string _hubName;
    private readonly string _callerId;
    private readonly ConcurrentDictionary<string, HashSet<string>> _connections;
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups;
    private readonly Action<string, string, string?, string> _dispatch;

    public WasmHubCallerClients(
        string hubName, string callerId,
        ConcurrentDictionary<string, HashSet<string>> connections,
        ConcurrentDictionary<string, HashSet<string>> groups,
        Action<string, string, string?, string> dispatch)
    {
        _hubName = hubName;
        _callerId = callerId;
        _connections = connections;
        _groups = groups;
        _dispatch = dispatch;
    }

    public IClientProxy All => new WasmClientProxy(_hubName, null, _dispatch);
    public IClientProxy Caller => new WasmClientProxy(_hubName, _callerId, _dispatch);
    public IClientProxy Others => new FilteredClientProxy(_hubName, _callerId, _connections, _dispatch);
    public IClientProxy Client(string connectionId) => new WasmClientProxy(_hubName, connectionId, _dispatch);
    public IClientProxy Group(string groupName) => new GroupClientProxy(_hubName, groupName, _groups, _dispatch);
}

/// <summary>
/// Sends to a specific connection or all (null = broadcast)
/// </summary>
internal class WasmClientProxy : IClientProxy
{
    private readonly string _hubName;
    private readonly string? _connectionId;
    private readonly Action<string, string, string?, string> _dispatch;

    public WasmClientProxy(string hubName, string? connectionId, Action<string, string, string?, string> dispatch)
    {
        _hubName = hubName;
        _connectionId = connectionId;
        _dispatch = dispatch;
    }

    public Task SendAsync(string method, params object?[] args)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(args);
        _dispatch(_hubName, method, _connectionId, json);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends to all except the caller
/// </summary>
internal class FilteredClientProxy : IClientProxy
{
    private readonly string _hubName;
    private readonly string _excludeId;
    private readonly ConcurrentDictionary<string, HashSet<string>> _connections;
    private readonly Action<string, string, string?, string> _dispatch;

    public FilteredClientProxy(string hubName, string excludeId,
        ConcurrentDictionary<string, HashSet<string>> connections,
        Action<string, string, string?, string> dispatch)
    {
        _hubName = hubName;
        _excludeId = excludeId;
        _connections = connections;
        _dispatch = dispatch;
    }

    public Task SendAsync(string method, params object?[] args)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(args);
        if (_connections.TryGetValue(_hubName, out var conns))
        {
            foreach (var cid in conns.Where(c => c != _excludeId))
                _dispatch(_hubName, method, cid, json);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends to all connections in a group
/// </summary>
internal class GroupClientProxy : IClientProxy
{
    private readonly string _hubName;
    private readonly string _groupName;
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups;
    private readonly Action<string, string, string?, string> _dispatch;

    public GroupClientProxy(string hubName, string groupName,
        ConcurrentDictionary<string, HashSet<string>> groups,
        Action<string, string, string?, string> dispatch)
    {
        _hubName = hubName;
        _groupName = groupName;
        _groups = groups;
        _dispatch = dispatch;
    }

    public Task SendAsync(string method, params object?[] args)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(args);
        var key = $"{_hubName}:{_groupName}";
        if (_groups.TryGetValue(key, out var members))
        {
            foreach (var cid in members)
                _dispatch(_hubName, method, cid, json);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// WASM implementation of group manager
/// </summary>
internal class WasmGroupManager : IGroupManager
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups;

    public WasmGroupManager(ConcurrentDictionary<string, HashSet<string>> groups)
    {
        _groups = groups;
    }

    public Task AddToGroupAsync(string connectionId, string groupName)
    {
        var set = _groups.GetOrAdd(groupName, _ => new HashSet<string>());
        set.Add(connectionId);
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName)
    {
        if (_groups.TryGetValue(groupName, out var set))
            set.Remove(connectionId);
        return Task.CompletedTask;
    }
}
