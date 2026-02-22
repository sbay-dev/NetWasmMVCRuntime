using System.Text.Json;

namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Represents an action result that returns a JSON-formatted response
/// </summary>
public class JsonResult : IActionResult
{
    public object? Value { get; set; }
    
    public JsonResult(object? value)
    {
        Value = value;
    }
    
    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = 200;
        context.ContentType = "application/json";
        context.ResponseBody = Value != null 
            ? JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true })
            : "null";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents an action result that returns an OK (200) status with optional content
/// </summary>
public class OkObjectResult : IActionResult
{
    public object? Value { get; set; }
    
    public OkObjectResult(object? value)
    {
        Value = value;
    }
    
    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = 200;
        context.ContentType = "application/json";
        context.ResponseBody = Value != null 
            ? JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true })
            : "null";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents an action result that returns content with a specific content type
/// </summary>
public class ContentResult : IActionResult
{
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain";
    
    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = 200;
        context.ContentType = ContentType;
        context.ResponseBody = Content;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents an action result that returns a NotFound (404) status
/// </summary>
public class NotFoundResult : IActionResult
{
    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = 404;
        context.ContentType = "text/plain";
        context.ResponseBody = "Not Found";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents an action result that performs a client-side redirect in the SPA.
/// </summary>
public class RedirectResult : IActionResult
{
    public string Url { get; set; }

    public RedirectResult(string url)
    {
        Url = url;
    }

    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = 302;
        context.ContentType = "text/plain";
        context.ResponseBody = Url;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Represents an action result that returns a NotFound (404) status with content
/// </summary>
public class NotFoundObjectResult : IActionResult
{
    public object? Value { get; set; }

    public NotFoundObjectResult(object? value)
    {
        Value = value;
    }

    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = 404;
        context.ContentType = "application/json";
        context.ResponseBody = Value != null
            ? JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true })
            : "Not Found";
        return Task.CompletedTask;
    }
}
