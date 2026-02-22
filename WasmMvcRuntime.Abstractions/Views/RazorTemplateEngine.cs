using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;

namespace WasmMvcRuntime.Abstractions.Views;

/// <summary>
/// Simple Razor-like template engine for WebAssembly
/// Supports basic .cshtml-like syntax
/// </summary>
public interface IRazorTemplateEngine
{
    /// <summary>
    /// Renders a Razor template with a model
    /// </summary>
    Task<string> RenderAsync(string template, object? model, IDictionary<string, object?>? viewData);

    /// <summary>
    /// Renders a view body inside a layout, applying _ViewStart layout assignment
    /// </summary>
    Task<string> RenderWithLayoutAsync(string viewBody, string? layoutTemplate, object? model, IDictionary<string, object?>? viewData);
}

/// <summary>
/// Implementation of a simple Razor template engine
/// </summary>
public class RazorTemplateEngine : IRazorTemplateEngine
{
    private ITemplateProvider? _templateProvider;

    /// <summary>Set the template provider for resolving partial views.</summary>
    public void SetTemplateProvider(ITemplateProvider provider) => _templateProvider = provider;
    public async Task<string> RenderAsync(string template, object? model, IDictionary<string, object?>? viewData)
    {
        var html = template;

        try
        {
            // Handle @model directive
            html = Regex.Replace(html, @"@model\s+[\w\.]+", "", RegexOptions.Multiline);

            // Handle @using directives
            html = Regex.Replace(html, @"@using\s+[\w\.]+", "", RegexOptions.Multiline);

            // Handle @addTagHelper directives
            html = Regex.Replace(html, @"@addTagHelper\s+.+", "", RegexOptions.Multiline);

            // Extract and remove @section blocks (store for later use)
            var sections = ExtractSections(ref html);

            // Handle @{ } code blocks - extract ViewData assignments first
            html = ProcessCodeBlocks(html, viewData);

            // Handle @Model.Property expressions
            if (model != null)
            {
                html = ReplaceModelExpressions(html, model);
            }

            // Handle @ViewData["key"] expressions
            if (viewData != null)
            {
                html = ReplaceViewDataExpressions(html, viewData);
            }

            // Handle @if statements
            html = await ProcessConditionals(html, model, viewData);

            // Handle @foreach loops
            html = await ProcessLoops(html, model);

            // Handle partial views: @await Html.PartialAsync("_Name") and @Html.Partial("_Name")
            html = await ProcessPartials(html, model, viewData);

            // Handle simple @ expressions like @DateTime.Now
            html = ReplaceSimpleExpressions(html);

            // Process AnchorTagHelper: <a asp-controller="X" asp-action="Y"> → <a href="/x/y">
            html = ProcessAnchorTagHelpers(html);

            return await Task.FromResult(html);
        }
        catch (Exception ex)
        {
            return $"<div class='error'>Template rendering error: {ex.Message}</div>";
        }
    }

    public async Task<string> RenderWithLayoutAsync(string viewBody, string? layoutTemplate, object? model, IDictionary<string, object?>? viewData)
    {
        if (string.IsNullOrEmpty(layoutTemplate))
        {
            return await RenderAsync(viewBody, model, viewData);
        }

        // First render the view body
        var sections = ExtractSections(ref viewBody);
        var renderedBody = await RenderAsync(viewBody, model, viewData);

        // Now render the layout with @RenderBody() replaced by the view content
        var html = layoutTemplate;

        // Process directives in layout
        html = Regex.Replace(html, @"@model\s+[\w\.]+", "", RegexOptions.Multiline);
        html = Regex.Replace(html, @"@using\s+[\w\.]+", "", RegexOptions.Multiline);
        html = Regex.Replace(html, @"@addTagHelper\s+.+", "", RegexOptions.Multiline);
        html = ProcessCodeBlocks(html, viewData);

        // Replace @RenderBody() with the rendered view content
        html = Regex.Replace(html, @"@RenderBody\(\)", renderedBody);

        // Replace @RenderSection("name", required: false/true)
        html = Regex.Replace(html, @"@(?:await\s+)?RenderSection(?:Async)?\(\s*""([^""]+)""\s*(?:,\s*required\s*:\s*(true|false)\s*)?\)",
            match =>
            {
                var sectionName = match.Groups[1].Value;
                if (sections.TryGetValue(sectionName, out var sectionContent))
                {
                    return sectionContent;
                }
                return "";
            });

        // Handle @ViewData["key"] in layout
        if (viewData != null)
        {
            html = ReplaceViewDataExpressions(html, viewData);
        }

        // Process conditionals in layout
        html = await ProcessConditionals(html, model, viewData);

        // Process partial views in layout
        html = await ProcessPartials(html, model, viewData);

        // Process AnchorTagHelper in layout
        html = ProcessAnchorTagHelpers(html);

        // Process simple expressions
        html = ReplaceSimpleExpressions(html);

        // SPA mode: extract only <body> inner content.
        // The host index.html already provides <html>/<head>/<body>,
        // so we strip the document wrapper to avoid nested documents in innerHTML.
        html = ExtractBodyContent(html);

        return html;
    }

    /// <summary>
    /// Extracts inner content of &lt;body&gt; from a full HTML document.
    /// Preserves &lt;link rel="stylesheet"&gt; and &lt;style&gt; tags from &lt;head&gt;
    /// so the client-side activateScripts can promote them to the real document head.
    /// If no &lt;body&gt; tag found, returns the original HTML unchanged.
    /// </summary>
    private static string ExtractBodyContent(string html)
    {
        var bodyOpen = Regex.Match(html, @"<body[^>]*>", RegexOptions.IgnoreCase);
        if (!bodyOpen.Success) return html;

        var bodyClose = Regex.Match(html, @"</body\s*>", RegexOptions.IgnoreCase);
        if (!bodyClose.Success) return html;

        // Preserve <link rel="stylesheet"> and <style> from <head>
        var headResources = new StringBuilder();
        var headOpen = Regex.Match(html, @"<head[^>]*>", RegexOptions.IgnoreCase);
        var headClose = Regex.Match(html, @"</head\s*>", RegexOptions.IgnoreCase);
        if (headOpen.Success && headClose.Success)
        {
            var headStart = headOpen.Index + headOpen.Length;
            var headContent = html.Substring(headStart, headClose.Index - headStart);

            foreach (Match m in Regex.Matches(headContent,
                @"<link\b[^>]*rel\s*=\s*""stylesheet""[^>]*/?\s*>", RegexOptions.IgnoreCase))
                headResources.AppendLine(m.Value);

            foreach (Match m in Regex.Matches(headContent,
                @"<style\b[^>]*>.*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                headResources.AppendLine(m.Value);
        }

        var start = bodyOpen.Index + bodyOpen.Length;
        var length = bodyClose.Index - start;
        var body = html.Substring(start, length).Trim();

        return headResources.Length > 0
            ? headResources.ToString() + body
            : body;
    }

    /// <summary>
    /// Extracts @section Name { ... } blocks from the template
    /// </summary>
    private Dictionary<string, string> ExtractSections(ref string html)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Match @section Name { content } with balanced braces
        html = Regex.Replace(html, @"@section\s+(\w+)\s*\{((?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}",
            match =>
            {
                var name = match.Groups[1].Value;
                var content = match.Groups[2].Value.Trim();
                sections[name] = content;
                return "";
            }, RegexOptions.Singleline);

        return sections;
    }

    /// <summary>
    /// Processes @{ } code blocks, extracting ViewData/ViewBag assignments
    /// </summary>
    private string ProcessCodeBlocks(string html, IDictionary<string, object?>? viewData)
    {
        return Regex.Replace(html, @"@\{((?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}", match =>
        {
            var code = match.Groups[1].Value;

            if (viewData != null)
            {
                // Extract ViewData["Key"] = "Value"; assignments
                var assignments = Regex.Matches(code, @"ViewData\[""([^""]+)""\]\s*=\s*""([^""]*)""\s*;");
                foreach (Match assignment in assignments)
                {
                    var key = assignment.Groups[1].Value;
                    var value = assignment.Groups[2].Value;
                    viewData[key] = value;
                }
            }

            // Extract Layout = "name"; (we just remove it, layout is handled by the pipeline)
            return "";
        }, RegexOptions.Singleline);
    }

    /// <summary>
    /// Processes AnchorTagHelper attributes on &lt;a&gt; tags
    /// </summary>
    private string ProcessAnchorTagHelpers(string html)
    {
        // Match <a ... asp-controller="X" ... asp-action="Y" ...>
        var pattern = @"<a\b([^>]*?)(?:\s*/>|>)";

        return Regex.Replace(html, pattern, match =>
        {
            var fullTag = match.Value;
            var attrs = match.Groups[1].Value;

            // Extract asp-controller and asp-action
            var controllerMatch = Regex.Match(attrs, @"asp-controller\s*=\s*""([^""]*)""");
            var actionMatch = Regex.Match(attrs, @"asp-action\s*=\s*""([^""]*)""");
            var areaMatch = Regex.Match(attrs, @"asp-area\s*=\s*""([^""]*)""");

            if (!controllerMatch.Success && !actionMatch.Success)
                return fullTag;

            var controller = controllerMatch.Success ? controllerMatch.Groups[1].Value : "home";
            var action = actionMatch.Success ? actionMatch.Groups[1].Value : "index";
            var href = $"/{controller.ToLowerInvariant()}/{action.ToLowerInvariant()}";

            // Remove asp-* attributes
            var cleanedAttrs = Regex.Replace(attrs, @"\s*asp-[\w-]+\s*=\s*""[^""]*""", "");

            // Add or replace href
            if (Regex.IsMatch(cleanedAttrs, @"href\s*="))
            {
                cleanedAttrs = Regex.Replace(cleanedAttrs, @"href\s*=\s*""[^""]*""", $@"href=""{href}""");
            }
            else
            {
                cleanedAttrs = $@" href=""{href}""" + cleanedAttrs;
            }

            var isSelfClosing = fullTag.TrimEnd().EndsWith("/>");
            return isSelfClosing ? $"<a{cleanedAttrs} />" : $"<a{cleanedAttrs}>";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }

    private string ReplaceModelExpressions(string html, object model)
    {
        // Match @Model.PropertyName or @Model?.PropertyName
        var pattern = @"@Model\??\.(\w+)";
        
        return Regex.Replace(html, pattern, match =>
        {
            var propertyName = match.Groups[1].Value;
            var property = model.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            
            if (property != null)
            {
                var value = property.GetValue(model);
                return value?.ToString() ?? "";
            }
            
            return match.Value; // Keep original if not found
        });
    }

    private string ReplaceViewDataExpressions(string html, IDictionary<string, object?> viewData)
    {
        // Match @ViewData["key"]
        var pattern = @"@ViewData\[""([^""]+)""\]";
        
        html = Regex.Replace(html, pattern, match =>
        {
            var key = match.Groups[1].Value;
            if (viewData.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        });

        // Match @ViewBag.Property
        var viewBagPattern = @"@ViewBag\.(\w+)";
        
        html = Regex.Replace(html, viewBagPattern, match =>
        {
            var key = match.Groups[1].Value;
            if (viewData.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        });

        return html;
    }

    private async Task<string> ProcessConditionals(string html, object? model, IDictionary<string, object?>? viewData)
    {
        // @if (Model != null) { ... }
        var modelPattern = @"@if\s*\(Model\s*!=\s*null\)\s*\{([^}]*)\}";
        html = Regex.Replace(html, modelPattern, match =>
        {
            var content = match.Groups[1].Value;
            return model != null ? content : "";
        }, RegexOptions.Singleline);

        // @if (ViewBag.Property != null) { ... }
        var viewBagPattern = @"@if\s*\(\s*ViewBag\.(\w+)\s*!=\s*null\s*\)\s*\{((?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}";
        html = Regex.Replace(html, viewBagPattern, match =>
        {
            var key = match.Groups[1].Value;
            var content = match.Groups[2].Value;
            if (viewData != null && viewData.TryGetValue(key, out var value) && value != null)
                return content;
            return "";
        }, RegexOptions.Singleline);

        return await Task.FromResult(html);
    }

    /// <summary>
    /// Processes @await Html.PartialAsync("_Name") and @Html.Partial("_Name")
    /// Resolves partial from shared templates and renders inline with current ViewData.
    /// </summary>
    private async Task<string> ProcessPartials(string html, object? model, IDictionary<string, object?>? viewData)
    {
        if (_templateProvider == null) return html;

        // Match @await Html.PartialAsync("_Name") or @Html.Partial("_Name")
        var pattern = @"@(?:await\s+Html\.PartialAsync|Html\.Partial)\(\s*""([^""]+)""\s*\)";
        var matches = Regex.Matches(html, pattern);

        // Process in reverse to preserve indices
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var partialName = match.Groups[1].Value;

            var partialTemplate = await _templateProvider.GetSharedTemplateAsync(partialName);
            if (!string.IsNullOrEmpty(partialTemplate))
            {
                var rendered = await RenderAsync(partialTemplate, model, viewData);
                html = html.Remove(match.Index, match.Length).Insert(match.Index, rendered);
            }
            else
            {
                html = html.Remove(match.Index, match.Length)
                    .Insert(match.Index, $"<!-- partial '{partialName}' not found -->");
            }
        }

        return html;
    }

    private async Task<string> ProcessLoops(string html, object? model)
    {
        // @foreach (var item in Model) { ... }
        var pattern = @"@foreach\s*\(\s*var\s+(\w+)\s+in\s+Model\s*\)\s*\{((?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}";

        var match = Regex.Match(html, pattern, RegexOptions.Singleline);
        if (match.Success && model is System.Collections.IEnumerable enumerable)
        {
            var itemVar = match.Groups[1].Value;
            var bodyTemplate = match.Groups[2].Value;
            var sb = new StringBuilder();
            int index = 0;

            foreach (var item in enumerable)
            {
                index++;
                var row = bodyTemplate;

                // Replace @item.Property
                row = Regex.Replace(row, $@"@{itemVar}\.(\w+)", m =>
                {
                    var prop = m.Groups[1].Value;
                    var pi = item.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                    if (pi != null)
                    {
                        var val = pi.GetValue(item);
                        return val?.ToString() ?? "";
                    }
                    return m.Value;
                });

                // Replace @index (row number)
                row = Regex.Replace(row, @"@index", index.ToString());

                sb.Append(row);
            }

            html = html.Remove(match.Index, match.Length).Insert(match.Index, sb.ToString());
        }

        return await Task.FromResult(html);
    }

    private string ReplaceSimpleExpressions(string html)
    {
        // Replace @DateTime.Now
        html = Regex.Replace(html, @"@DateTime\.Now", DateTime.Now.ToString());
        
        // Replace @DateTime.Now.ToString("format")
        var datePattern = @"@DateTime\.Now\.ToString\(""([^""]+)""\)";
        html = Regex.Replace(html, datePattern, match =>
        {
            var format = match.Groups[1].Value;
            return DateTime.Now.ToString(format);
        });

        return html;
    }
}

/// <summary>
/// File-based template provider for .cshtml files
/// </summary>
public interface ITemplateProvider
{
    /// <summary>
    /// Gets a template by controller and view name
    /// </summary>
    Task<string?> GetTemplateAsync(string controllerName, string viewName);

    /// <summary>
    /// Gets a shared template (e.g., _Layout)
    /// </summary>
    Task<string?> GetSharedTemplateAsync(string templateName);

    /// <summary>
    /// Gets the _ViewStart.cshtml content
    /// </summary>
    Task<string?> GetViewStartAsync();

    /// <summary>
    /// Gets the _ViewImports.cshtml content
    /// </summary>
    Task<string?> GetViewImportsAsync();
}

/// <summary>
/// Embedded resource-based template provider
/// </summary>
public class EmbeddedTemplateProvider : ITemplateProvider
{
    private readonly Dictionary<string, string> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sharedTemplates = new(StringComparer.OrdinalIgnoreCase);
    private string? _viewStart;
    private string? _viewImports;

    public EmbeddedTemplateProvider()
    {
        LoadEmbeddedTemplates();
    }

    private void LoadEmbeddedTemplates()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var assembly in assemblies)
        {
            try
            {
                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(name => name.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase));

                foreach (var resourceName in resourceNames)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var content = reader.ReadToEnd();
                        
                        var parts = resourceName.Split('.');

                        // Check for Areas: ...Areas.{Area}.Views.{Controller}.{View}.cshtml
                        var areasIndex = Array.FindIndex(parts, p => p.Equals("Areas", StringComparison.OrdinalIgnoreCase));
                        if (areasIndex >= 0)
                        {
                            var viewsIndex = Array.FindIndex(parts, areasIndex, p => p.Equals("Views", StringComparison.OrdinalIgnoreCase));
                            if (viewsIndex >= 0 && areasIndex + 1 < viewsIndex)
                            {
                                var area = parts[areasIndex + 1];
                                ProcessViewResource(parts, viewsIndex, content, $"Areas/{area}");
                            }
                            continue;
                        }

                        // Standard Views: ...Views.{Controller}.{View}.cshtml
                        var stdViewsIndex = Array.FindIndex(parts, p => p.Equals("Views", StringComparison.OrdinalIgnoreCase));
                        if (stdViewsIndex >= 0)
                        {
                            ProcessViewResource(parts, stdViewsIndex, content, null);
                        }
                    }
                }
            }
            catch
            {
                // Skip problematic assemblies
            }
        }
    }

    private void ProcessViewResource(string[] parts, int viewsIndex, string content, string? areaPrefix)
    {
        // _ViewStart.cshtml
        if (viewsIndex + 1 < parts.Length && parts[viewsIndex + 1] == "_ViewStart")
        {
            _viewStart ??= content;
            return;
        }

        // _ViewImports.cshtml
        if (viewsIndex + 1 < parts.Length && parts[viewsIndex + 1] == "_ViewImports")
        {
            _viewImports ??= content;
            return;
        }

        // Shared templates: ...Views.Shared._Layout.cshtml
        if (viewsIndex + 1 < parts.Length && parts[viewsIndex + 1].Equals("Shared", StringComparison.OrdinalIgnoreCase)
            && viewsIndex + 2 < parts.Length)
        {
            var templateName = parts[viewsIndex + 2];
            _sharedTemplates.TryAdd(templateName, content);
            return;
        }

        // Regular view: ...Views.Controller.Action.cshtml
        if (viewsIndex + 2 < parts.Length)
        {
            var controller = parts[viewsIndex + 1];
            var view = parts[viewsIndex + 2];
            var key = areaPrefix != null
                ? $"{areaPrefix}/{controller}/{view}"
                : $"{controller}/{view}";
            _templates[key] = content;
        }
    }

    public Task<string?> GetTemplateAsync(string controllerName, string viewName)
    {
        var key = $"{controllerName}/{viewName}";
        _templates.TryGetValue(key, out var template);
        return Task.FromResult(template);
    }

    public Task<string?> GetSharedTemplateAsync(string templateName)
    {
        _sharedTemplates.TryGetValue(templateName, out var template);
        return Task.FromResult(template);
    }

    public Task<string?> GetViewStartAsync()
    {
        return Task.FromResult(_viewStart);
    }

    public Task<string?> GetViewImportsAsync()
    {
        return Task.FromResult(_viewImports);
    }
}
