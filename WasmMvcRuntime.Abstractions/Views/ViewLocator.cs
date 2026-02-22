using Microsoft.AspNetCore.Components;

namespace WasmMvcRuntime.Abstractions.Views;

/// <summary>
/// Service for locating and rendering Razor component views
/// </summary>
public interface IViewLocator
{
    /// <summary>
    /// Finds a view component by controller and action name
    /// </summary>
    Type? FindView(string controllerName, string? viewName = null);

    /// <summary>
    /// Checks if a view exists
    /// </summary>
    bool ViewExists(string controllerName, string? viewName = null);
}

/// <summary>
/// Default implementation of IViewLocator
/// </summary>
public class ViewLocator : IViewLocator
{
    private readonly Dictionary<string, Type> _viewCache = new();
    private bool _initialized = false;

    public ViewLocator()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;

        // Scan all assemblies for Razor components in Views folders
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var assembly in assemblies)
        {
            try
            {
                var componentTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && 
                               !t.IsAbstract && 
                               typeof(IComponent).IsAssignableFrom(t) &&
                               (t.Namespace?.Contains(".Views") ?? false));

                foreach (var type in componentTypes)
                {
                    // Extract controller and view name from namespace and type name
                    // Example: MyApp.Views.Home.Index -> Controller: Home, View: Index
                    var namespaceParts = type.Namespace?.Split('.') ?? Array.Empty<string>();
                    var viewsIndex = Array.FindIndex(namespaceParts, p => p == "Views");
                    
                    if (viewsIndex >= 0 && viewsIndex < namespaceParts.Length - 1)
                    {
                        var controllerName = namespaceParts[viewsIndex + 1];
                        var viewName = type.Name;
                        
                        var key = $"{controllerName}/{viewName}".ToLowerInvariant();
                        _viewCache[key] = type;
                    }
                }
            }
            catch
            {
                // Skip problematic assemblies
            }
        }

        _initialized = true;
    }

    public Type? FindView(string controllerName, string? viewName = null)
    {
        var key = $"{controllerName}/{viewName ?? "Index"}".ToLowerInvariant();
        return _viewCache.TryGetValue(key, out var type) ? type : null;
    }

    public bool ViewExists(string controllerName, string? viewName = null)
    {
        return FindView(controllerName, viewName) != null;
    }
}
