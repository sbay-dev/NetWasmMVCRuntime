using System.Text.Json;
using System.Web;
using WasmMvcRuntime.Abstractions;

namespace WasmMvcRuntime.Cepha.Http;

/// <summary>
/// Extended HTTP context for the Cepha server runtime.
/// Adds real HTTP semantics: headers, cookies, query parameters, form body,
/// and response headers — on top of the base <see cref="InternalHttpContext"/>.
/// </summary>
public class CephaHttpContext : InternalHttpContext
{
    /// <summary>Unique request identifier assigned by the JS host.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Incoming request headers.</summary>
    public Dictionary<string, string> RequestHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parsed query-string parameters.</summary>
    public Dictionary<string, string> QueryParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parsed form data (for POST application/x-www-form-urlencoded).</summary>
    public Dictionary<string, string> FormData { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw request body string.</summary>
    public string? RawBody { get; set; }

    /// <summary>Parsed cookies from the Cookie header.</summary>
    public Dictionary<string, string> Cookies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Outgoing response headers to send back to the client.</summary>
    public Dictionary<string, string> ResponseHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The client's remote IP address (if provided by the JS host).</summary>
    public string? RemoteAddress { get; set; }

    /// <summary>
    /// Creates a CephaHttpContext from the raw JS request parameters.
    /// </summary>
    public static CephaHttpContext FromRequest(
        string requestId,
        string method,
        string path,
        string? headersJson,
        string? bodyContent,
        IServiceProvider? requestServices = null)
    {
        var ctx = new CephaHttpContext
        {
            RequestId = requestId,
            Method = method.ToUpperInvariant(),
            RawBody = bodyContent,
            RequestServices = requestServices
        };

        // ??? Parse path and query string ?????????????????????
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            ctx.Path = path[..queryIndex];
            var queryString = path[(queryIndex + 1)..];
            var parsed = HttpUtility.ParseQueryString(queryString);
            foreach (string? key in parsed)
            {
                if (key != null)
                    ctx.QueryParameters[key] = parsed[key] ?? "";
            }
        }
        else
        {
            ctx.Path = path;
        }

        // ??? Parse request headers ???????????????????????????
        if (!string.IsNullOrEmpty(headersJson))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                if (headers != null)
                    ctx.RequestHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        // ??? Parse cookies from Cookie header ????????????????
        if (ctx.RequestHeaders.TryGetValue("cookie", out var cookieHeader) && !string.IsNullOrEmpty(cookieHeader))
        {
            foreach (var segment in cookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = segment.IndexOf('=');
                if (eqIdx > 0)
                {
                    var name = segment[..eqIdx].Trim();
                    var value = segment[(eqIdx + 1)..].Trim();
                    ctx.Cookies[name] = value;
                }
            }
        }

        // ??? Parse form data (POST with form content type) ???
        if (ctx.Method == "POST" && !string.IsNullOrEmpty(bodyContent))
        {
            var contentType = ctx.RequestHeaders.GetValueOrDefault("content-type", "");
            if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = HttpUtility.ParseQueryString(bodyContent);
                foreach (string? key in parsed)
                {
                    if (key != null)
                        ctx.FormData[key] = parsed[key] ?? "";
                }
            }
        }

        // ??? Extract remote address ??????????????????????????
        ctx.RemoteAddress = ctx.RequestHeaders.GetValueOrDefault("x-forwarded-for")
                         ?? ctx.RequestHeaders.GetValueOrDefault("x-real-ip");

        // ??? Default CORS response headers ???????????????????
        ctx.ResponseHeaders["Access-Control-Allow-Origin"] = "*";
        ctx.ResponseHeaders["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
        ctx.ResponseHeaders["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
        ctx.ResponseHeaders["X-Powered-By"] = "Cepha/1.0 (WasmMvcRuntime)";

        return ctx;
    }

    /// <summary>
    /// Serializes the response as a JSON envelope for the JS host.
    /// </summary>
    public string ToResponseJson()
    {
        var envelope = new
        {
            statusCode = StatusCode,
            contentType = ContentType,
            body = ResponseBody,
            headers = ResponseHeaders
        };

        return JsonSerializer.Serialize(envelope);
    }
}
