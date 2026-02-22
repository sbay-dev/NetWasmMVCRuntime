 
namespace WasmMvcRuntime.Abstractions.Mvc;

/// <summary>
/// A filter that runs before and after an action method executes.
/// </summary>
public interface IActionFilter
{
    void OnActionExecuting(ActionExecutingContext context);
    void OnActionExecuted(ActionExecutedContext context);
}

/// <summary>
/// A filter that asynchronously surrounds execution of the action.
/// </summary>
public interface IAsyncActionFilter
{
    Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next);
}

/// <summary>
/// A delegate that represents the execution of an action.
/// </summary>
public delegate Task<ActionExecutedContext> ActionExecutionDelegate();

/// <summary>
/// Context for action executing.
/// </summary>
public class ActionExecutingContext : ActionContext
{
    public IDictionary<string, object?> ActionArguments { get; set; } = new Dictionary<string, object?>();
    public WasmMvcRuntime.Abstractions.IActionResult? Result { get; set; }
}

/// <summary>
/// Context for action executed.
/// </summary>
public class ActionExecutedContext : ActionContext
{
    public WasmMvcRuntime.Abstractions.IActionResult? Result { get; set; }
    public Exception? Exception { get; set; }
    public bool ExceptionHandled { get; set; }
}

/// <summary>
/// Context for an action.
/// </summary>
public class ActionContext
{
    public WasmHttpContext? HttpContext { get; set; }
    public ModelStateDictionary ModelState { get; set; } = new();
}
