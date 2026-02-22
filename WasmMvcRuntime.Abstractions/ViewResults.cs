using WasmMvcRuntime.Abstractions.Mvc;
using WasmMvcRuntime.Abstractions.Views;

namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Represents an action result that renders a view to the response.
/// </summary>
public class ViewResult : IActionResult
{
    private static IViewLocator? _viewLocator;
    private static IViewRenderer? _viewRenderer;
    private static ITemplateProvider? _templateProvider;

    /// <summary>
    /// Gets or sets the name of the view to render.
    /// </summary>
    public string? ViewName { get; set; }

    /// <summary>
    /// Gets the view data model.
    /// </summary>
    public object? Model => ViewData?.Model;

    /// <summary>
    /// Gets or sets the ViewData dictionary.
    /// </summary>
    public ViewDataDictionary? ViewData { get; set; }

    /// <summary>
    /// Gets or sets the TempData dictionary.
    /// </summary>
    public ITempDataDictionary? TempData { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the Content-Type header for the response.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the controller name for view location.
    /// </summary>
    public string? ControllerName { get; set; }

    /// <summary>
    /// Gets or sets the area name for view location.
    /// </summary>
    public string? Area { get; set; }

    /// <summary>
    /// Configures the view locator and renderer (called during app initialization).
    /// </summary>
    public static void Configure(IViewLocator? viewLocator = null, IViewRenderer? viewRenderer = null, ITemplateProvider? templateProvider = null)
    {
        _templateProvider = templateProvider ?? new EmbeddedTemplateProvider();
        var engine = new RazorTemplateEngine();
        engine.SetTemplateProvider(_templateProvider);
        _viewLocator = viewLocator ?? new ViewLocator();
        _viewRenderer = viewRenderer ?? new ViewRenderer(engine);
    }

    public async Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = StatusCode ?? 200;
        context.ContentType = ContentType ?? "text/html";

        // Ensure services are initialized
        if (_viewLocator == null || _viewRenderer == null || _templateProvider == null)
        {
            Configure();
        }

        try
        {
            // Determine the controller name from context or default
            var controllerName = ControllerName ?? "Home";
            var viewName = ViewName ?? "Index";

            // First, try to find a .cshtml template
            // Area views: Areas/{area}/Views/{controller}/{view}
            // Standard views: Views/{controller}/{view}
            string? template = null;
            if (!string.IsNullOrEmpty(Area))
            {
                template = await _templateProvider!.GetTemplateAsync($"Areas/{Area}/{controllerName}", viewName);
            }
            template ??= await _templateProvider!.GetTemplateAsync(controllerName, viewName);
            
            string html;
            
            if (!string.IsNullOrEmpty(template))
            {
                // Render .cshtml template with layout support
                var model = ViewData?.Model;
                var viewDataDict = ViewData as IDictionary<string, object?>;

                // Determine layout: per-view @{ Layout = "..."; } takes priority over _ViewStart
                var layoutName = ExtractLayoutFromTemplate(template) ?? await GetLayoutNameAsync();
                string? layoutTemplate = null;
                if (!string.IsNullOrEmpty(layoutName))
                {
                    layoutTemplate = await _templateProvider.GetSharedTemplateAsync(layoutName);
                }

                if (!string.IsNullOrEmpty(layoutTemplate))
                {
                    html = await _viewRenderer!.RenderTemplateWithLayoutAsync(template, layoutTemplate, model, viewDataDict);
                }
                else
                {
                    html = await _viewRenderer!.RenderTemplateAsync(template, model, viewDataDict);
                    html = GenerateCompleteHtml(viewName, html);
                }

                context.ResponseBody = html;
            }
            else
            {
                // Fall back to Blazor component (.razor)
                var viewType = _viewLocator!.FindView(controllerName, viewName);

                if (viewType == null)
                {
                    // View not found - return error message
                    context.ResponseBody = GenerateViewNotFoundHtml(controllerName, viewName);
                    return;
                }

                // Render the view with model and viewdata
                var model = ViewData?.Model;
                var viewDataDict = ViewData as IDictionary<string, object?>;

                html = await _viewRenderer!.RenderViewAsync(viewType, model, viewDataDict);
                
                // Wrap in complete HTML document
                context.ResponseBody = GenerateCompleteHtml(viewName, html);
            }
        }
        catch (Exception ex)
        {
            context.ResponseBody = GenerateErrorHtml(ex);
        }
    }

    /// <summary>
    /// Extracts Layout assignment from a view template's @{ } code block.
    /// Per-view layout overrides _ViewStart.cshtml, matching ASP.NET Core MVC behavior.
    /// </summary>
    private static string? ExtractLayoutFromTemplate(string template)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            template, @"Layout\s*=\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reads _ViewStart.cshtml to determine the layout name
    /// </summary>
    private async Task<string?> GetLayoutNameAsync()
    {
        var viewStart = await _templateProvider!.GetViewStartAsync();
        if (string.IsNullOrEmpty(viewStart)) return null;

        // Extract Layout = "_Layout"; from _ViewStart
        var match = System.Text.RegularExpressions.Regex.Match(viewStart, @"Layout\s*=\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private string GenerateCompleteHtml(string title, string content)
    {
        // In SPA mode, only return the content — the host index.html provides <head>/<link> tags
        return $@"<div class=""container mt-3"">
    {content}
</div>";
    }

    private string GenerateViewNotFoundHtml(string controllerName, string viewName)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"" dir=""ltr"">
<head>
    <meta charset=""utf-8"" />
    <title>View Not Found</title>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; padding: 20px; }}
        .error {{ color: #dc3545; background-color: #f8d7da; border: 1px solid #f5c6cb; padding: 15px; border-radius: 5px; }}
        code {{ background-color: #f8f9fa; padding: 2px 6px; border-radius: 3px; }}
    </style>
</head>
<body>
    <div class=""error"">
        <h2>View Not Found</h2>
        <p>The view <strong>{viewName}</strong> was not found for controller <strong>{controllerName}</strong>.</p>
        <p>Expected path:</p>
        <ul>
            <li><code>Views/{controllerName}/{viewName}.razor</code> (Blazor Component)</li>
            <li><code>Views/{controllerName}/{viewName}.cshtml</code> (Razor Template)</li>
        </ul>
        <hr />
        <h3>How to create this view:</h3>
        <h4>Option 1: Blazor Component (.razor)</h4>
        <ol>
            <li>Create the folder: <code>Views/{controllerName}/</code></li>
            <li>Add the file: <code>{viewName}.razor</code></li>
            <li>Use <code>@inherits RazorViewBase&lt;TModel&gt;</code></li>
        </ol>
        <h4>Option 2: Razor Template (.cshtml)</h4>
        <ol>
            <li>Create the folder: <code>Views/{controllerName}/</code></li>
            <li>Add the file: <code>{viewName}.cshtml</code></li>
            <li>Use <code>@model YourModelType</code></li>
            <li>Set Build Action = Embedded Resource</li>
        </ol>
    </div>
</body>
</html>";
    }

    private string GenerateErrorHtml(Exception ex)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"" dir=""ltr"">
<head>
    <meta charset=""utf-8"" />
    <title>View Rendering Error</title>
    <style>
        body {{ font-family: Arial, sans-serif; padding: 20px; }}
        .error {{ color: #dc3545; background-color: #f8d7da; border: 1px solid #f5c6cb; padding: 15px; border-radius: 5px; }}
        pre {{ background-color: #f8f9fa; padding: 10px; border-radius: 3px; overflow-x: auto; }}
    </style>
</head>
<body>
    <div class=""error"">
        <h2>View Rendering Error</h2>
        <p><strong>Message:</strong> {ex.Message}</p>
        <h3>Stack Trace:</h3>
        <pre>{ex.StackTrace}</pre>
    </div>
</body>
</html>";
    }
}

/// <summary>
/// Represents an action result that renders a partial view to the response.
/// Partial views are rendered WITHOUT layout wrapping, matching ASP.NET Core MVC behavior.
/// </summary>
public class PartialViewResult : IActionResult
{
    private static IViewRenderer? _viewRenderer;
    private static ITemplateProvider? _templateProvider;

    /// <summary>
    /// Gets or sets the name of the partial view to render.
    /// </summary>
    public string? ViewName { get; set; }

    /// <summary>
    /// Gets the view data model.
    /// </summary>
    public object? Model => ViewData?.Model;

    /// <summary>
    /// Gets or sets the ViewData dictionary.
    /// </summary>
    public ViewDataDictionary? ViewData { get; set; }

    /// <summary>
    /// Gets or sets the TempData dictionary.
    /// </summary>
    public ITempDataDictionary? TempData { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the Content-Type header for the response.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the controller name for view location.
    /// </summary>
    public string? ControllerName { get; set; }

    /// <summary>
    /// Gets or sets the area name for view location.
    /// </summary>
    public string? Area { get; set; }

    public async Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = StatusCode ?? 200;
        context.ContentType = ContentType ?? "text/html";

        if (_templateProvider == null || _viewRenderer == null)
        {
            _templateProvider = new EmbeddedTemplateProvider();
            var engine = new RazorTemplateEngine();
            engine.SetTemplateProvider(_templateProvider);
            _viewRenderer = new ViewRenderer(engine);
        }

        try
        {
            var controllerName = ControllerName ?? "Home";
            var viewName = ViewName ?? "Index";

            string? template = null;
            if (!string.IsNullOrEmpty(Area))
            {
                template = await _templateProvider.GetTemplateAsync($"Areas/{Area}/{controllerName}", viewName);
            }
            template ??= await _templateProvider.GetTemplateAsync(controllerName, viewName);
            // Also try Shared folder for partial views (e.g., _LoginPartial)
            template ??= await _templateProvider.GetSharedTemplateAsync(viewName);

            if (!string.IsNullOrEmpty(template))
            {
                var model = ViewData?.Model;
                var viewDataDict = ViewData as IDictionary<string, object?>;
                // Partial views render WITHOUT layout
                context.ResponseBody = await _viewRenderer.RenderTemplateAsync(template, model, viewDataDict);
            }
            else
            {
                context.ResponseBody = $"<!-- Partial view '{viewName}' not found -->";
            }
        }
        catch (Exception ex)
        {
            context.ResponseBody = $"<div class=\"error\">Partial view error: {ex.Message}</div>";
        }
    }
}

/// <summary>
/// Represents an action result that renders a view component to the response.
/// </summary>
public class ViewComponentResult : IActionResult
{
    /// <summary>
    /// Gets or sets the name of the view component to invoke.
    /// </summary>
    public string? ViewComponentName { get; set; }

    /// <summary>
    /// Gets or sets the type of the view component to invoke.
    /// </summary>
    public Type? ViewComponentType { get; set; }

    /// <summary>
    /// Gets or sets the arguments to pass to the view component.
    /// </summary>
    public object? Arguments { get; set; }

    /// <summary>
    /// Gets the view data model.
    /// </summary>
    public object? Model => ViewData?.Model;

    /// <summary>
    /// Gets or sets the ViewData dictionary.
    /// </summary>
    public ViewDataDictionary? ViewData { get; set; }

    /// <summary>
    /// Gets or sets the TempData dictionary.
    /// </summary>
    public ITempDataDictionary? TempData { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the Content-Type header for the response.
    /// </summary>
    public string? ContentType { get; set; }

    public Task ExecuteResultAsync(IInternalHttpContext context)
    {
        context.StatusCode = StatusCode ?? 200;
        context.ContentType = ContentType ?? "text/html";
        
        var componentName = ViewComponentName ?? ViewComponentType?.Name ?? "Unknown";
        context.ResponseBody = $"ViewComponent: {componentName}";
        
        return Task.CompletedTask;
    }
}
