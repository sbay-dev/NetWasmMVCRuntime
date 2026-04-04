using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions;

namespace WasmMvcRuntime.Core;

/// <summary>
/// MVC Engine implementation that scans and executes controller actions
/// </summary>
public class MvcEngine : IMvcEngine
{
    private readonly Dictionary<string, ControllerActionDescriptor> _routes = new();
    private readonly IServiceProvider? _serviceProvider;

    public MvcEngine(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
        ScanControllers();
    }

    /// <summary>
    /// Scans all loaded assemblies for controllers inheriting from ControllerBase
    /// </summary>
    private void ScanControllers()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var assembly in assemblies)
        {
            try
            {
                var controllerTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

                foreach (var controllerType in controllerTypes)
                {
                    RegisterController(controllerType);
                }
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }
        }
    }

    /// <summary>
    /// Registers a controller and its action methods
    /// </summary>
    private void RegisterController(Type controllerType)
    {
        var controllerName = controllerType.Name.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);
        
        // Check for [Area] attribute
        var areaAttr = controllerType.GetCustomAttribute<AreaAttribute>();
        var areaPrefix = areaAttr != null ? areaAttr.RouteValue : null;

        // Check for [Route] attributes on class (supports multiple, ordered)
        var classRoutes = controllerType.GetCustomAttributes<RouteAttribute>()
            .OrderBy(r => r.Order).ToArray();
        var isApiController = controllerType.GetCustomAttribute<ApiControllerAttribute>() != null;
        
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !m.GetCustomAttributes<NonActionAttribute>().Any());

        foreach (var method in methods)
        {
            // Check for [Route] attributes on method (supports multiple, ordered)
            var methodRoutes = method.GetCustomAttributes<RouteAttribute>()
                .OrderBy(r => r.Order).ToArray();

            // Check for Http* attribute templates (HttpGet, HttpPost, etc.)
            var httpTemplates = method.GetCustomAttributes()
                .OfType<IRouteTemplateProvider>()
                .Where(p => p is not RouteAttribute && !string.IsNullOrEmpty(p.Template))
                .Select(p => p.Template!)
                .ToArray();

            if (methodRoutes.Length > 0)
            {
                // Method-level [Route] attributes define absolute routes
                foreach (var mRoute in methodRoutes)
                {
                    var template = mRoute.Template
                        .Replace("[area]", areaPrefix ?? "", StringComparison.OrdinalIgnoreCase)
                        .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase);
                    var route = NormalizeRoute(template);
                    RegisterRoute(route, controllerType, method, isApiController, areaPrefix);
                }
            }
            else if (httpTemplates.Length > 0)
            {
                // Http* attribute templates (e.g., [HttpGet("api/guests")])
                foreach (var template in httpTemplates)
                {
                    var expanded = template
                        .Replace("[area]", areaPrefix ?? "", StringComparison.OrdinalIgnoreCase)
                        .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[action]", method.Name, StringComparison.OrdinalIgnoreCase);
                    var route = NormalizeRoute(expanded);
                    RegisterRoute(route, controllerType, method, isApiController, areaPrefix);
                }
            }
            else if (classRoutes.Length > 0)
            {
                // Class-level [Route] with method name or Http*Attribute template
                foreach (var cRoute in classRoutes)
                {
                    var baseRoute = cRoute.Template
                        .Replace("[area]", areaPrefix ?? "", StringComparison.OrdinalIgnoreCase)
                        .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase);
                    
                    var methodTemplate = GetMethodRouteTemplate(method);
                    var route = !string.IsNullOrEmpty(methodTemplate)
                        ? NormalizeRoute($"{baseRoute}/{methodTemplate}")
                        : NormalizeRoute($"{baseRoute}/{method.Name}");
                    RegisterRoute(route, controllerType, method, isApiController, areaPrefix);
                }
            }
            else
            {
                // Convention routing: /area/controller/action or /controller/action
                var route = areaPrefix != null
                    ? NormalizeRoute($"{areaPrefix}/{controllerName}/{method.Name}")
                    : NormalizeRoute($"{controllerName}/{method.Name}");
                RegisterRoute(route, controllerType, method, isApiController, areaPrefix);
            }
        }
    }

    private static string NormalizeRoute(string route)
    {
        route = "/" + route.TrimStart('/');
        route = route.Replace("//", "/").ToLowerInvariant();
        return route;
    }

    private void RegisterRoute(string route, Type controllerType, MethodInfo method, bool isApiController, string? area = null)
    {
        _routes[route] = new ControllerActionDescriptor
        {
            ControllerType = controllerType,
            ActionMethod = method,
            IsApiController = isApiController,
            Area = area
        };
    }

    private string? GetMethodRouteTemplate(MethodInfo method)
    {
        // Use IRouteTemplateProvider to handle all Http* attributes uniformly
        var providers = method.GetCustomAttributes()
            .OfType<IRouteTemplateProvider>()
            .Where(p => p is not RouteAttribute && p.Template != null)
            .OrderBy(p => p.Order ?? 0);

        return providers.FirstOrDefault()?.Template;
    }

    /// <summary>
    /// Processes an HTTP request through the MVC pipeline
    /// </summary>
    public async Task ProcessRequestAsync(IInternalHttpContext context)
    {
        try
        {
            var path = context.Path.ToLowerInvariant().TrimEnd('/');
            
            // Remove query strings if any
            var queryIndex = path.IndexOf('?');
            if (queryIndex >= 0)
            {
                path = path.Substring(0, queryIndex);
            }

            if (string.IsNullOrEmpty(path)) path = "/";

            // ─── Smart Route Resolution (cascading fallback) ─────
            var (descriptor, routeValues) = ResolveRouteWithParams(path);

            if (descriptor == null)
            {
                context.StatusCode = 404;
                context.ContentType = "text/plain";
                context.ResponseBody = $"Route not found: {context.Path}";
                return;
            }

            // Create controller instance via DI or Activator
            var provider = context.RequestServices ?? _serviceProvider;
            var controller = CreateControllerInstance(descriptor.ControllerType, provider);
            
            if (controller == null)
            {
                context.StatusCode = 500;
                context.ContentType = "text/plain";
                context.ResponseBody = "Failed to create controller instance";
                return;
            }

            // Set controller context (HttpContext, FormData)
            if (controller is Controller mvcController)
            {
                mvcController.ControllerContext = new Abstractions.Mvc.ControllerContext
                {
                    HttpContext = new Abstractions.Mvc.WasmHttpContext
                    {
                        RequestServices = provider
                    }
                };

                // Merge context.Items into ViewData (session info, etc.)
                if (context is InternalHttpContext ctx && ctx.Items.Count > 0)
                {
                    foreach (var kvp in ctx.Items)
                        mvcController.ViewData[kvp.Key] = kvp.Value;
                }
            }
            if (controller is ControllerBase controllerBase)
            {
                controllerBase.Form = context.FormData;
            }

            // Invoke action method with route parameters and body binding
            var methodParams = descriptor.ActionMethod.GetParameters();
            object?[]? args = null;
            if (methodParams.Length > 0)
            {
                args = new object?[methodParams.Length];
                for (int i = 0; i < methodParams.Length; i++)
                {
                    var p = methodParams[i];
                    if (p.ParameterType == typeof(CancellationToken))
                    {
                        args[i] = CancellationToken.None;
                    }
                    else if (p.GetCustomAttributes().Any(a => a.GetType().Name == "FromBodyAttribute")
                             && !string.IsNullOrEmpty(context.RequestBody))
                    {
                        try
                        {
                            args[i] = System.Text.Json.JsonSerializer.Deserialize(
                                context.RequestBody, p.ParameterType,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        catch { args[i] = p.HasDefaultValue ? p.DefaultValue : null; }
                    }
                    else if (routeValues.TryGetValue(p.Name?.ToLowerInvariant() ?? "", out var rv))
                    {
                        args[i] = Convert.ChangeType(rv, p.ParameterType);
                    }
                    else if (context.FormData.TryGetValue(p.Name ?? "", out var fv))
                    {
                        args[i] = Convert.ChangeType(fv, p.ParameterType);
                    }
                    else
                    {
                        args[i] = p.HasDefaultValue ? p.DefaultValue : null;
                    }
                }
            }
            var result = descriptor.ActionMethod.Invoke(controller, args);
            
            // Handle async methods
            if (result is Task task)
            {
                await task;
                
                var resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty != null)
                {
                    result = resultProperty.GetValue(task);
                }
                else
                {
                    result = null;
                }
            }

            // Execute IActionResult
            if (result is IActionResult actionResult)
            {
                // Auto-set ViewName, ControllerName and Area from action descriptor
                if (actionResult is ViewResult viewResult)
                {
                    if (string.IsNullOrEmpty(viewResult.ViewName))
                        viewResult.ViewName = descriptor.ActionMethod.Name;
                    if (string.IsNullOrEmpty(viewResult.ControllerName))
                        viewResult.ControllerName = descriptor.ControllerType.Name.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(viewResult.Area))
                        viewResult.Area = descriptor.Area;
                }
                else if (actionResult is PartialViewResult partialResult)
                {
                    if (string.IsNullOrEmpty(partialResult.ViewName))
                        partialResult.ViewName = descriptor.ActionMethod.Name;
                    if (string.IsNullOrEmpty(partialResult.ControllerName))
                        partialResult.ControllerName = descriptor.ControllerType.Name.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(partialResult.Area))
                        partialResult.Area = descriptor.Area;
                }

                await actionResult.ExecuteResultAsync(context);
            }
            else if (result != null)
            {
                // For non-IActionResult returns, serialize as JSON
                context.StatusCode = 200;
                context.ContentType = "application/json";
                context.ResponseBody = System.Text.Json.JsonSerializer.Serialize(result, 
                    WasmMvcRuntime.Abstractions.CephaJsonDefaults.Options);
            }
            else
            {
                context.StatusCode = 204;
                context.ResponseBody = string.Empty;
            }

            // Dispose controller if disposable
            if (controller is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            context.StatusCode = 500;
            context.ContentType = "text/plain";
            context.ResponseBody = $"Error: {inner.Message}\n\nStack Trace:\n{inner.StackTrace}";
        }
    }

    /// <summary>
    /// Creates a controller instance using DI if available, otherwise Activator
    /// </summary>
    private object? CreateControllerInstance(Type controllerType, IServiceProvider? provider)
    {
        if (provider != null)
        {
            try
            {
                return ActivatorUtilities.CreateInstance(provider, controllerType);
            }
            catch
            {
                // Fall back to parameterless constructor
            }
        }
        return Activator.CreateInstance(controllerType);
    }

    /// <summary>
    /// Gets all registered routes
    /// </summary>
    public IReadOnlyDictionary<string, ControllerActionDescriptor> GetRoutes() => _routes;

    /// <summary>
    /// Smart route resolution with cascading fallback:
    /// 1. Exact match (e.g., /benchmark/stress)
    /// 2. Parameterized match (e.g., /api/guests/{id}/freeze)
    /// 3. Append /index (e.g., /benchmark → /benchmark/index)
    /// 4. Convention: /{controller}/{action} mapping
    /// 5. Root "/" → /home/index → first controller's first action
    /// </summary>
    private (ControllerActionDescriptor?, Dictionary<string, string>) ResolveRouteWithParams(string path)
    {
        var empty = new Dictionary<string, string>();

        // 1. Exact match
        if (_routes.TryGetValue(path, out var exact))
            return (exact, empty);

        // 2. Root "/" → cascading fallback
        if (path == "/")
        {
            if (_routes.TryGetValue("/home/index", out var homeIndex))
                return (homeIndex, empty);
            if (_routes.Count > 0)
                return (_routes.Values.First(), empty);
            return (null, empty);
        }

        // 3. Parameterized route matching (e.g., /api/guests/abc123/freeze matches /api/guests/{id}/freeze)
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var kvp in _routes)
        {
            var routeTemplate = kvp.Key;
            if (!routeTemplate.Contains('{')) continue;

            var routeSegments = routeTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (routeSegments.Length != pathSegments.Length) continue;

            bool match = true;
            var values = new Dictionary<string, string>();
            for (int i = 0; i < routeSegments.Length; i++)
            {
                if (routeSegments[i].StartsWith('{') && routeSegments[i].EndsWith('}'))
                {
                    var paramName = routeSegments[i].Trim('{', '}').ToLowerInvariant();
                    values[paramName] = pathSegments[i];
                }
                else if (!string.Equals(routeSegments[i], pathSegments[i], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (match) return (kvp.Value, values);
        }

        // 4. Append /index
        var withIndex = path + "/index";
        if (_routes.TryGetValue(withIndex, out var indexed))
            return (indexed, empty);

        // 5. Convention
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 1)
        {
            var conventionIndex = $"/{segments[0]}/index";
            if (_routes.TryGetValue(conventionIndex, out var convIdx))
                return (convIdx, empty);
        }
        else if (segments.Length >= 2)
        {
            var areaIndex = $"/{segments[0]}/{segments[1]}/index";
            if (_routes.TryGetValue(areaIndex, out var areaIdx))
                return (areaIdx, empty);
        }

        return (null, empty);
    }
}

/// <summary>
/// Describes a controller action for routing
/// </summary>
public class ControllerActionDescriptor
{
    public Type ControllerType { get; set; } = null!;
    public MethodInfo ActionMethod { get; set; } = null!;
    public bool IsApiController { get; set; }
    public string? Area { get; set; }
}
