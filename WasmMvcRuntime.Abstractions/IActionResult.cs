namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Defines a contract for action results
/// </summary>
public interface IActionResult
{
    /// <summary>
    /// Executes the result operation of the action method asynchronously
    /// </summary>
    /// <param name="context">The HTTP context</param>
    Task ExecuteResultAsync(IInternalHttpContext context);
}
