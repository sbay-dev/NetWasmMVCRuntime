namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Represents an internal HTTP context for WASM MVC operations
/// </summary>
public interface IInternalHttpContext
{
    /// <summary>
    /// Gets or sets the request path
    /// </summary>
    string Path { get; set; }
    
    /// <summary>
    /// Gets or sets the HTTP method
    /// </summary>
    string Method { get; set; }
    
    /// <summary>
    /// Gets or sets the response status code
    /// </summary>
    int StatusCode { get; set; }
    
    /// <summary>
    /// Gets or sets the response content type
    /// </summary>
    string ContentType { get; set; }
    
    /// <summary>
    /// Gets or sets the response body
    /// </summary>
    string ResponseBody { get; set; }

    /// <summary>
    /// Gets or sets form data submitted via POST
    /// </summary>
    IDictionary<string, string> FormData { get; }

    /// <summary>
    /// Gets or sets the scoped service provider for this request
    /// </summary>
    IServiceProvider? RequestServices { get; set; }
}
