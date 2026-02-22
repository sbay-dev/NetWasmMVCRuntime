namespace WasmMvcRuntime.Abstractions.Mvc;

/// <summary>
/// Encapsulates all HTTP-specific information about an individual HTTP request for WASM.
/// </summary>
public class WasmHttpContext
{
    public IServiceProvider? RequestServices { get; set; }
    public WasmHttpRequest Request { get; set; } = new WasmHttpRequest();
    public WasmHttpResponse Response { get; set; } = new WasmHttpResponse();
    public string TraceIdentifier { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Represents the incoming HTTP request.
/// </summary>
public class WasmHttpRequest
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public IQueryCollection Query { get; set; } = new QueryCollection();
}

/// <summary>
/// Represents the outgoing HTTP response.
/// </summary>
public class WasmHttpResponse
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "text/html";
}

/// <summary>
/// Represents a collection of query string values.
/// </summary>
public interface IQueryCollection : IEnumerable<KeyValuePair<string, string>>
{
    string? this[string key] { get; }
}

/// <summary>
/// Default implementation of IQueryCollection.
/// </summary>
public class QueryCollection : IQueryCollection
{
    private readonly Dictionary<string, string> _data = new();

    public string? this[string key] => _data.TryGetValue(key, out var value) ? value : null;

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _data.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Context object for execution of action which has been selected as part of an HTTP request.
/// </summary>
public class ControllerContext
{
    public WasmHttpContext? HttpContext { get; set; }
    public ModelStateDictionary ModelState { get; set; } = new();
}

/// <summary>
/// Extension methods for IServiceProvider.
/// </summary>
public static class ServiceProviderServiceExtensions
{
    public static T? GetRequiredService<T>(this IServiceProvider provider) where T : class
    {
        return provider.GetService(typeof(T)) as T;
    }
}
