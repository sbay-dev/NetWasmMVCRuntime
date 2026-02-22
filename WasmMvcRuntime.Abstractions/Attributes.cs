namespace WasmMvcRuntime.Abstractions;

/// <summary>
/// Interface for attributes which can supply a route template for attribute routing.
/// Matches the official Microsoft.AspNetCore.Mvc.Routing.IRouteTemplateProvider contract.
/// </summary>
public interface IRouteTemplateProvider
{
    string? Template { get; }
    int? Order { get; }
    string? Name { get; }
}

/// <summary>
/// Indicates that a method is not an action method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class NonActionAttribute : Attribute
{
}

/// <summary>
/// Specifies an attribute route on a controller.
/// Matches the official Microsoft.AspNetCore.Mvc.RouteAttribute contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RouteAttribute : Attribute, IRouteTemplateProvider
{
    public string Template { get; }
    public int Order { get; set; }
    public string? Name { get; set; }

    int? IRouteTemplateProvider.Order => Order;

    public RouteAttribute(string template)
    {
        Template = template;
    }
}

/// <summary>
/// Indicates that a controller serves HTTP API responses (JSON by default).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ApiControllerAttribute : Attribute
{
}

/// <summary>
/// Specifies that a class or method supports the HTTP GET method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpGetAttribute : Attribute, IRouteTemplateProvider
{
    public string? Template { get; set; }
    public int Order { get; set; }
    public string? Name { get; set; }

    int? IRouteTemplateProvider.Order => Order;

    public HttpGetAttribute()
    {
    }

    public HttpGetAttribute(string template)
    {
        Template = template;
    }
}

/// <summary>
/// Specifies that a class or method supports the HTTP POST method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpPostAttribute : Attribute, IRouteTemplateProvider
{
    public string? Template { get; set; }
    public int Order { get; set; }
    public string? Name { get; set; }

    int? IRouteTemplateProvider.Order => Order;

    public HttpPostAttribute()
    {
    }

    public HttpPostAttribute(string template)
    {
        Template = template;
    }
}

/// <summary>
/// Specifies that a class or method supports the HTTP PUT method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpPutAttribute : Attribute, IRouteTemplateProvider
{
    public string? Template { get; set; }
    public int Order { get; set; }
    public string? Name { get; set; }

    int? IRouteTemplateProvider.Order => Order;

    public HttpPutAttribute()
    {
    }

    public HttpPutAttribute(string template)
    {
        Template = template;
    }
}

/// <summary>
/// Specifies that a class or method supports the HTTP DELETE method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpDeleteAttribute : Attribute, IRouteTemplateProvider
{
    public string? Template { get; set; }
    public int Order { get; set; }
    public string? Name { get; set; }

    int? IRouteTemplateProvider.Order => Order;

    public HttpDeleteAttribute()
    {
    }

    public HttpDeleteAttribute(string template)
    {
        Template = template;
    }
}

/// <summary>
/// Specifies the area that a controller belongs to.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AreaAttribute : Attribute
{
    public string RouteValue { get; }

    public AreaAttribute(string routeValue)
    {
        RouteValue = routeValue;
    }
}

/// <summary>
/// Specifies the response cache behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ResponseCacheAttribute : Attribute
{
    public int Duration { get; set; }
    public ResponseCacheLocation Location { get; set; }
    public bool NoStore { get; set; }
}

/// <summary>
/// Specifies the location of the response cache.
/// </summary>
public enum ResponseCacheLocation
{
    Any,
    Client,
    None
}

/// <summary>
/// Marks a property as a view data dictionary.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
internal sealed class ViewDataDictionaryAttribute : Attribute
{
}
