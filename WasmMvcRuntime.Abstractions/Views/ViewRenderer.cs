using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System.Reflection;

namespace WasmMvcRuntime.Abstractions.Views;

/// <summary>
/// Service for rendering Razor component views with models
/// </summary>
public interface IViewRenderer
{
    /// <summary>
    /// Renders a view component to HTML
    /// </summary>
    Task<string> RenderViewAsync(Type viewType, object? model, IDictionary<string, object?>? viewData);
    
    /// <summary>
    /// Renders a .cshtml template to HTML
    /// </summary>
    Task<string> RenderTemplateAsync(string template, object? model, IDictionary<string, object?>? viewData);

    /// <summary>
    /// Renders a .cshtml template inside a layout
    /// </summary>
    Task<string> RenderTemplateWithLayoutAsync(string template, string layoutTemplate, object? model, IDictionary<string, object?>? viewData);
}

/// <summary>
/// Default implementation of IViewRenderer for WebAssembly
/// </summary>
public class ViewRenderer : IViewRenderer
{
    private readonly IRazorTemplateEngine _templateEngine;

    public ViewRenderer(IRazorTemplateEngine? templateEngine = null)
    {
        _templateEngine = templateEngine ?? new RazorTemplateEngine();
    }

    public async Task<string> RenderViewAsync(Type viewType, object? model, IDictionary<string, object?>? viewData)
    {
        try
        {
            // Create a render fragment for the component
            var parameters = new Dictionary<string, object?>();
            
            // Add Model parameter if the component accepts it
            if (model != null)
            {
                var modelProperty = viewType.GetProperty("Model");
                if (modelProperty != null)
                {
                    parameters["Model"] = model;
                }
            }

            // Add ViewData if the component accepts it
            if (viewData != null && viewData.Count > 0)
            {
                var viewDataProperty = viewType.GetProperty("ViewData");
                if (viewDataProperty != null)
                {
                    parameters["ViewData"] = viewData;
                }
            }

            // Build HTML representation
            var html = $@"
<!-- Rendered View: {viewType.Name} -->
<div class=""view-container"" data-view=""{viewType.Name}"">
    <h2>View: {viewType.Name}</h2>
    {(model != null ? $"<div class=\"model-info\"><strong>Model Type:</strong> {model.GetType().Name}</div>" : "")}
    <div class=""view-content"">
        <!-- View content would be rendered here by Blazor component -->
        {RenderModelProperties(model)}
    </div>
</div>";

            return await Task.FromResult(html);
        }
        catch (Exception ex)
        {
            return $"<div class=\"error\">Error rendering view: {ex.Message}</div>";
        }
    }

    public async Task<string> RenderTemplateAsync(string template, object? model, IDictionary<string, object?>? viewData)
    {
        return await _templateEngine.RenderAsync(template, model, viewData);
    }

    public async Task<string> RenderTemplateWithLayoutAsync(string template, string layoutTemplate, object? model, IDictionary<string, object?>? viewData)
    {
        return await _templateEngine.RenderWithLayoutAsync(template, layoutTemplate, model, viewData);
    }

    private string RenderModelProperties(object? model)
    {
        if (model == null) return "<p>No model data</p>";

        var properties = model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (properties.Length == 0) return "<p>Model has no properties</p>";

        var html = "<table class=\"table model-properties\"><thead><tr><th>Property</th><th>Value</th></tr></thead><tbody>";
        
        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(model);
                html += $"<tr><td>{prop.Name}</td><td>{value ?? "null"}</td></tr>";
            }
            catch
            {
                html += $"<tr><td>{prop.Name}</td><td><em>Error reading value</em></td></tr>";
            }
        }
        
        html += "</tbody></table>";
        return html;
    }
}
