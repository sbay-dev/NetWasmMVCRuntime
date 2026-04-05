using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// ═══════════════════════════════════════════════════════════════════
// 🧬 Browser-wasm shims for Microsoft.AspNetCore.Http
//    Provides the same API surface as ASP.NET Core's HTTP pipeline
//    so middleware code (app.Use) compiles unchanged across SDKs.
//    In WASM, requests bypass the middleware pipeline (routed directly
//    via CephaApp → MvcEngine), so these types are used for API parity.
// ═══════════════════════════════════════════════════════════════════

namespace Microsoft.AspNetCore.Http
{
    /// <summary>Encapsulates all HTTP-specific information about an individual request.</summary>
    public class HttpContext
    {
        public HttpRequest Request { get; } = new();
        public HttpResponse Response { get; } = new();
        public IServiceProvider RequestServices { get; set; } = null!;
    }

    /// <summary>Represents the incoming side of an individual HTTP request.</summary>
    public class HttpRequest
    {
        public PathString Path { get; set; } = new("/");
        public QueryString QueryString { get; set; } = default;
        public string Method { get; set; } = "GET";
        public Stream Body { get; set; } = Stream.Null;
        public string? ContentType { get; set; }
        public long? ContentLength { get; set; }
        public HttpHeaderDictionary Headers { get; } = new();

        /// <summary>Enables request body buffering. In WASM the body is already in-memory.</summary>
        public void EnableBuffering() { }
    }

    /// <summary>Represents the outgoing side of an individual HTTP response.</summary>
    public class HttpResponse
    {
        public int StatusCode { get; set; } = 200;
        public HttpHeaderDictionary Headers { get; } = new();
        public Stream Body { get; set; } = new MemoryStream();

        public Task WriteAsync(string text, CancellationToken ct = default)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            return Body.WriteAsync(bytes, 0, bytes.Length);
        }
    }

    /// <summary>Represents the host portion of a URI as a PathString value.</summary>
    public readonly struct PathString
    {
        public string? Value { get; }
        public PathString(string? value) => Value = value;
        public override string ToString() => Value ?? "";
        public static implicit operator string(PathString p) => p.Value ?? "";
    }

    /// <summary>Provides correct handling for QueryString value when needed to reconstruct a request URL.</summary>
    public readonly struct QueryString
    {
        public string? Value { get; }
        public QueryString(string? value) => Value = value;
        public override string ToString() => Value ?? "";
    }

    /// <summary>Contains static methods to check HTTP request method types.</summary>
    public static class HttpMethods
    {
        public static bool IsPost(string method) =>
            string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
        public static bool IsGet(string method) =>
            string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
        public static bool IsPut(string method) =>
            string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase);
        public static bool IsDelete(string method) =>
            string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Header dictionary matching ASP.NET Core IHeaderDictionary API surface.</summary>
    public class HttpHeaderDictionary : IEnumerable<KeyValuePair<string, StringValues>>
    {
        private readonly Dictionary<string, StringValues> _data = new(StringComparer.OrdinalIgnoreCase);

        public StringValues this[string key]
        {
            get => _data.TryGetValue(key, out var v) ? v : default;
            set => _data[key] = value;
        }

        public void Append(string key, string value)
        {
            if (_data.TryGetValue(key, out var existing))
                _data[key] = existing.Append(value);
            else
                _data[key] = new StringValues(value);
        }

        public bool TryGetValue(string key, out StringValues value) => _data.TryGetValue(key, out value);
        public bool ContainsKey(string key) => _data.ContainsKey(key);
        public bool Remove(string key) => _data.Remove(key);
        public int Count => _data.Count;

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Represents zero/one/many strings (header values). Matches Microsoft.Extensions.Primitives.StringValues.</summary>
    public readonly struct StringValues : IEnumerable<string>
    {
        private readonly string[]? _values;

        public StringValues(string value) => _values = new[] { value };
        public StringValues(string[] values) => _values = values;

        public int Count => _values?.Length ?? 0;
        public string[] ToArray() => _values ?? Array.Empty<string>();

        public StringValues Append(string value)
        {
            var existing = _values ?? Array.Empty<string>();
            var result = new string[existing.Length + 1];
            existing.CopyTo(result, 0);
            result[^1] = value;
            return new StringValues(result);
        }

        public override string ToString() => _values != null ? string.Join(", ", _values) : "";

        public static implicit operator string(StringValues sv) => sv.ToString();
        public static implicit operator string[](StringValues sv) => sv.ToArray();
        public static implicit operator StringValues(string value) => new(value);
        public static implicit operator StringValues(string[] values) => new(values);

        public IEnumerator<string> GetEnumerator() =>
            ((IEnumerable<string>)(_values ?? Array.Empty<string>())).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
