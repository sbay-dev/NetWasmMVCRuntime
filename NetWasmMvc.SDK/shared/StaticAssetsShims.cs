using System;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Cepha no-op route builder used to preserve WebApplication API surface without static-assets runtime dependency.
    /// </summary>
    public sealed class CephaEndpointConventionBuilder : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
            // Intentionally no-op.
        }
    }

    /// <summary>
    /// Cepha shim: neutralizes static-assets related APIs for browser-wasm.
    /// Keeps Program.cs unchanged while preventing runtime lookup of
    /// {AppName}.staticwebassets.endpoints.json.
    /// </summary>
    public static class CephaStaticAssetsWebApplicationExtensions
    {
        public static WebApplication MapStaticAssets(this WebApplication app, string? staticAssetsManifestPath = null)
        {
            return app;
        }

        public static CephaEndpointConventionBuilder MapControllerRoute(
            this WebApplication app,
            string name,
            string pattern,
            object? defaults = null,
            object? constraints = null,
            object? dataTokens = null)
        {
            return new CephaEndpointConventionBuilder();
        }

        public static CephaEndpointConventionBuilder WithStaticAssets(
            this CephaEndpointConventionBuilder builder,
            string? manifestPath = null)
        {
            return builder;
        }
    }
}

