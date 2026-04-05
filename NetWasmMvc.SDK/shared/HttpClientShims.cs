using System;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

// ═══════════════════════════════════════════════════════════════════
// 🧬 Browser-wasm shim for System.Net.Http.HttpClientHandler
//    In browser WASM the real HttpClientHandler throws
//    PlatformNotSupportedException — the browser manages redirects,
//    cookies, and TLS internally via the fetch API.
//    This shim allows code that creates HttpClientHandler/HttpClient
//    to compile and not crash at startup.
//    The proxy middleware is never invoked in WASM (requests go
//    through CephaApp → MvcEngine), so SendAsync returns 503.
// ═══════════════════════════════════════════════════════════════════

namespace System.Net.Http
{
    /// <summary>
    /// WASM-safe HttpClientHandler shim. All properties are accepted
    /// but ignored — the browser's fetch API handles redirects,
    /// cookies, and TLS natively.
    /// </summary>
    public class HttpClientHandler : HttpMessageHandler
    {
        public bool AllowAutoRedirect { get; set; } = true;
        public bool UseCookies { get; set; } = true;
        public CookieContainer CookieContainer { get; set; } = new();
        public int MaxAutomaticRedirections { get; set; } = 50;
        public DecompressionMethods AutomaticDecompression { get; set; }
        public bool UseProxy { get; set; } = true;
        public IWebProxy? Proxy { get; set; }
        public bool PreAuthenticate { get; set; }
        public bool UseDefaultCredentials { get; set; }
        public ICredentials? Credentials { get; set; }
        public long MaxRequestContentBufferSize { get; set; } = int.MaxValue;
        public int MaxConnectionsPerServer { get; set; } = int.MaxValue;
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(100);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Content = new StringContent("HTTP proxy requires server mode");
            return Task.FromResult(response);
        }
    }
}
