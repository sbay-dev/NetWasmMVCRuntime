namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Engine for processing MVC requests in WASM environment
/// </summary>
public interface IMvcEngine
{
    /// <summary>
    /// Processes an HTTP request through the MVC pipeline
    /// </summary>
    /// <param name="context">The internal HTTP context</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task ProcessRequestAsync(IInternalHttpContext context);
}
