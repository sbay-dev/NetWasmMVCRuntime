using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

// ═══════════════════════════════════════════════════════════════════
// 🧬 Browser-wasm shims for System.Net.Http types
//    HttpClientHandler is not supported in browser WASM — the browser
//    manages redirects, cookies, and TLS internally via the fetch API.
//    These shims allow code that creates HttpClientHandler/HttpClient
//    to compile and not crash at startup in WASM mode.
//    The actual HTTP calls are no-ops in WASM (middleware pipeline
//    is bypassed — requests go through CephaApp → MvcEngine).
// ═══════════════════════════════════════════════════════════════════

namespace System.Net.Http
{
    /// <summary>
    /// WASM-safe HttpClientHandler shim. In browser WASM the real
    /// HttpClientHandler throws PlatformNotSupportedException.
    /// Properties are accepted but ignored — the browser's fetch API
    /// handles redirects, cookies, and TLS.
    /// </summary>
    public class HttpClientHandler : HttpMessageHandler
    {
        public bool AllowAutoRedirect { get; set; } = true;
        public bool UseCookies { get; set; } = true;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // In WASM, HTTP proxy middleware is never invoked (requests bypass
            // the pipeline). Return a 503 to indicate server mode is required.
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
            response.Content = new StringContent("HTTP proxy requires server mode");
            return Task.FromResult(response);
        }
    }
}
