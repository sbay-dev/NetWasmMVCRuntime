namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Default implementation of IInternalHttpContext
/// </summary>
public class InternalHttpContext : IInternalHttpContext
{
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "text/plain";
    public string ResponseBody { get; set; } = string.Empty;
    public IDictionary<string, string> FormData { get; } = new Dictionary<string, string>();
    public IServiceProvider? RequestServices { get; set; }

    /// <summary>Per-request items (like ASP.NET HttpContext.Items). Merged into ViewData before rendering.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}
