#pragma warning disable CS0436 // Type conflicts with imported type

using System.Text.Json;
using WasmMvcRuntime.Abstractions;

namespace Microsoft.AspNetCore.Mvc
{
    // ═══════════════════════════════════════════════════════════
    // IActionResult — same contract as WASM, Microsoft.AspNetCore.Mvc namespace
    // ═══════════════════════════════════════════════════════════

    public interface IActionResult
    {
        Task ExecuteResultAsync(IInternalHttpContext context);
    }

    // ═══════════════════════════════════════════════════════════
    // Result types — inherit WASM implementations + our IActionResult
    // ═══════════════════════════════════════════════════════════

    public class ViewResult : WasmMvcRuntime.Abstractions.ViewResult, IActionResult { }

    public class JsonResult : WasmMvcRuntime.Abstractions.JsonResult, IActionResult
    {
        public JsonResult(object? value) : base(value) { }
    }

    public class NotFoundResult : WasmMvcRuntime.Abstractions.NotFoundResult, IActionResult { }

    public class NotFoundObjectResult : WasmMvcRuntime.Abstractions.NotFoundObjectResult, IActionResult
    {
        public NotFoundObjectResult(object? value) : base(value) { }
    }

    public class RedirectResult : WasmMvcRuntime.Abstractions.RedirectResult, IActionResult
    {
        public RedirectResult(string url) : base(url) { }
    }

    public class OkObjectResult : WasmMvcRuntime.Abstractions.OkObjectResult, IActionResult
    {
        public OkObjectResult(object? value) : base(value) { }
    }

    public class ContentResult : WasmMvcRuntime.Abstractions.ContentResult, IActionResult { }

    /// <summary>Bad-request (400) result with optional value body.</summary>
    public class BadRequestObjectResult : IActionResult, WasmMvcRuntime.Abstractions.IActionResult
    {
        public object? Value { get; set; }
        public BadRequestObjectResult(object? value) => Value = value;

        public Task ExecuteResultAsync(IInternalHttpContext context)
        {
            context.StatusCode = 400;
            context.ContentType = "application/json";
            context.ResponseBody = Value != null
                ? JsonSerializer.Serialize(Value, WasmMvcRuntime.Abstractions.CephaJsonDefaults.Options)
                : "Bad Request";
            return Task.CompletedTask;
        }
    }

    /// <summary>Arbitrary status-code result with optional value body.</summary>
    public class ObjectResult : IActionResult, WasmMvcRuntime.Abstractions.IActionResult
    {
        public int StatusCode { get; set; }
        public object? Value { get; set; }

        public ObjectResult(int statusCode, object? value = null)
        {
            StatusCode = statusCode;
            Value = value;
        }

        public Task ExecuteResultAsync(IInternalHttpContext context)
        {
            context.StatusCode = StatusCode;
            context.ContentType = "application/json";
            context.ResponseBody = Value != null
                ? JsonSerializer.Serialize(Value, WasmMvcRuntime.Abstractions.CephaJsonDefaults.Options)
                : "";
            return Task.CompletedTask;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // HTTP helpers — Controller.Response / Controller.Request
    // ═══════════════════════════════════════════════════════════

    /// <summary>String-keyed header dictionary matching ASP.NET Core IHeaderDictionary surface.</summary>
    public class HeaderDictionary : Dictionary<string, string>
    {
        public string? CacheControl
        {
            get => TryGetValue("Cache-Control", out var v) ? v : null;
            set { if (value != null) this["Cache-Control"] = value; else Remove("Cache-Control"); }
        }

        public string? Connection
        {
            get => TryGetValue("Connection", out var v) ? v : null;
            set { if (value != null) this["Connection"] = value; else Remove("Connection"); }
        }

        /// <summary>Appends a value to the header. Multi-value headers are comma-separated.</summary>
        public void Append(string key, string value)
        {
            if (TryGetValue(key, out var existing))
                this[key] = existing + ", " + value;
            else
                this[key] = value;
        }
    }

    /// <summary>Browser-WASM HTTP response with ASP.NET Core API surface.</summary>
    public class CephaHttpResponse
    {
        public int StatusCode { get; set; } = 200;
        public string? ContentType { get; set; }
        public HeaderDictionary Headers { get; } = new();

        private readonly MemoryStream _body = new();
        public Stream Body => _body;

        public async Task WriteAsync(string text, CancellationToken ct = default)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            await _body.WriteAsync(bytes, ct);
        }
    }

    /// <summary>Browser-WASM HTTP request with ASP.NET Core API surface.</summary>
    public class CephaHttpRequest
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public HeaderDictionary Headers { get; } = new();
        public Stream Body { get; set; } = Stream.Null;
        public string? ContentType { get; set; }
        public long? ContentLength { get; set; }
        public string QueryString { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════════════
    // ControllerBase — for API controllers that don't need views
    // ═══════════════════════════════════════════════════════════

    public abstract class ControllerBase : WasmMvcRuntime.Abstractions.ControllerBase
    {
        private CephaHttpResponse? _response;
        private CephaHttpRequest? _request;

        public CephaHttpResponse Response => _response ??= new();
        public CephaHttpRequest Request => _request ??= new();

        protected new NotFoundResult NotFound() => new();
        protected new NotFoundObjectResult NotFound(object value) => new(value);
        protected new OkObjectResult Ok(object? value = null) => new(value);
        protected new JsonResult Json(object? value) => new(value);
        protected new ContentResult Content(string content, string contentType = "text/html")
            => new() { Content = content, ContentType = contentType };
        protected new RedirectResult Redirect(string url) => new(url);
        protected BadRequestObjectResult BadRequest(object? value = null) => new(value);
        protected ObjectResult StatusCode(int statusCode, object? value = null) => new(statusCode, value);
    }

    // ═══════════════════════════════════════════════════════════
    // Controller — full MVC controller with view support
    // ═══════════════════════════════════════════════════════════

    public abstract class Controller : WasmMvcRuntime.Abstractions.Controller
    {
        private CephaHttpResponse? _response;
        private CephaHttpRequest? _request;

        public CephaHttpResponse Response => _response ??= new();
        public CephaHttpRequest Request => _request ??= new();

        // ── View() overrides (covariant return: our ViewResult → WASM ViewResult) ──

        public override ViewResult View()
            => View(viewName: null, model: ViewData.Model);

        public override ViewResult View(string? viewName)
            => View(viewName, model: ViewData.Model);

        public override ViewResult View(object? model)
            => View(viewName: null, model: model);

        public override ViewResult View(string? viewName, object? model)
        {
            ViewData.Model = model;
            var controllerName = GetType().Name
                .Replace("Controller", "", StringComparison.OrdinalIgnoreCase);
            return new ViewResult
            {
                ViewName = viewName,
                ViewData = ViewData,
                TempData = TempData,
                ControllerName = controllerName
            };
        }

        // ── Json() overrides ──

        public override JsonResult Json(object? data) => new(data);

        public override JsonResult Json(object? data, object? serializerSettings) => new(data);

        // ── Result methods (shadow base to return our types) ──

        protected new NotFoundResult NotFound() => new();
        protected new NotFoundObjectResult NotFound(object value) => new(value);
        protected new OkObjectResult Ok(object? value = null) => new(value);
        protected new ContentResult Content(string content, string contentType = "text/html")
            => new() { Content = content, ContentType = contentType };
        protected new RedirectResult Redirect(string url) => new(url);
        protected BadRequestObjectResult BadRequest(object? value = null) => new(value);
        protected ObjectResult StatusCode(int statusCode, object? value = null) => new(statusCode, value);
    }

    // ═══════════════════════════════════════════════════════════
    // Attributes — implement WASM IRouteTemplateProvider so
    // MvcEngine discovers routes via .OfType<IRouteTemplateProvider>()
    // ═══════════════════════════════════════════════════════════

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class HttpGetAttribute : Attribute, IRouteTemplateProvider
    {
        public string? Template { get; set; }
        public int Order { get; set; }
        public string? Name { get; set; }
        int? IRouteTemplateProvider.Order => Order;
        public HttpGetAttribute() { }
        public HttpGetAttribute(string template) => Template = template;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class HttpPostAttribute : Attribute, IRouteTemplateProvider
    {
        public string? Template { get; set; }
        public int Order { get; set; }
        public string? Name { get; set; }
        int? IRouteTemplateProvider.Order => Order;
        public HttpPostAttribute() { }
        public HttpPostAttribute(string template) => Template = template;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class HttpPutAttribute : Attribute, IRouteTemplateProvider
    {
        public string? Template { get; set; }
        public int Order { get; set; }
        public string? Name { get; set; }
        int? IRouteTemplateProvider.Order => Order;
        public HttpPutAttribute() { }
        public HttpPutAttribute(string template) => Template = template;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class HttpDeleteAttribute : Attribute, IRouteTemplateProvider
    {
        public string? Template { get; set; }
        public int Order { get; set; }
        public string? Name { get; set; }
        int? IRouteTemplateProvider.Order => Order;
        public HttpDeleteAttribute() { }
        public HttpDeleteAttribute(string template) => Template = template;
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class FromBodyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ResponseCacheAttribute : Attribute
    {
        public int Duration { get; set; }
        public ResponseCacheLocation Location { get; set; }
        public bool NoStore { get; set; }
    }

    public enum ResponseCacheLocation { Any, Client, None }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class NonActionAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RouteAttribute : Attribute, IRouteTemplateProvider
    {
        public string Template { get; }
        public int Order { get; set; }
        public string? Name { get; set; }
        int? IRouteTemplateProvider.Order => Order;
        public RouteAttribute(string template) => Template = template;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AreaAttribute : Attribute
    {
        public string RouteValue { get; }
        public AreaAttribute(string routeValue) => RouteValue = routeValue;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ApiControllerAttribute : Attribute { }
}
