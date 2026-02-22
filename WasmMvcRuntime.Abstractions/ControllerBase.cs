namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Base class for WASM MVC controllers
/// </summary>
public abstract class ControllerBase : IWasmController
{
    /// <summary>
    /// Form data submitted via POST. Set by the MVC engine before action invocation.
    /// </summary>
    public IDictionary<string, string> Form { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Returns a redirect result that triggers SPA client-side navigation.
    /// </summary>
    protected RedirectResult Redirect(string url)
    {
        return new RedirectResult(url);
    }

    /// <summary>
    /// Returns an OK result with the specified value
    /// </summary>
    protected OkObjectResult Ok(object? value = null)
    {
        return new OkObjectResult(value);
    }
    
    /// <summary>
    /// Returns a JSON result with the specified value
    /// </summary>
    protected JsonResult Json(object? value)
    {
        return new JsonResult(value);
    }
    
    /// <summary>
    /// Returns a content result with the specified HTML content
    /// </summary>
    protected ContentResult Content(string content, string contentType = "text/html")
    {
        return new ContentResult 
        { 
            Content = content, 
            ContentType = contentType 
        };
    }
    
    /// <summary>
    /// Returns a NotFound result
    /// </summary>
    protected NotFoundResult NotFound()
    {
        return new NotFoundResult();
    }

    /// <summary>
    /// Returns a NotFound result with a value
    /// </summary>
    protected NotFoundObjectResult NotFound(object value)
    {
        return new NotFoundObjectResult(value);
    }
}
