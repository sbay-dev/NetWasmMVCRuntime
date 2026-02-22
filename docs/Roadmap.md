# Roadmap

## Current State

| Component | Version | Status |
|-----------|---------|--------|
| **NetWasmMvc.SDK** | 1.0.6 | ✅ Published on NuGet |
| **Cepha.CLI** | 1.0.48 | ✅ Published on NuGet |
| **MVC Pipeline** | — | ✅ Controllers, Views, Routing, Areas |
| **Razor Engine** | — | ✅ @Model, @ViewData, @if, @foreach, layouts, partials, tag helpers |
| **EF Core + SQLite** | — | ✅ Full LINQ, OPFS persistence |
| **Identity System** | — | ✅ PBKDF2, HMAC sessions, fingerprint binding, lockout |
| **SignalR Engine** | — | ✅ In-process hubs, groups, client proxies |
| **Dual Hosting** | — | ✅ Browser SPA + Node.js/Edge API |
| **CLI Tooling** | — | ✅ Scaffolding, dev server, publish, interactive menu |
| **CI/CD** | — | ✅ GitHub Actions (build, publish-sdk, publish-cli, deploy-pages) |

---

## Planned Features

### Short-Term

| Feature | Description | Priority |
|---------|-------------|----------|
| **Model Binding** | Automatic parameter binding from form data / query strings to action parameters | High |
| **Full Tag Helper support** | `asp-for`, `asp-validation-for`, `asp-items` and remaining tag helpers | High |
| **View Components** | Full `@await Component.InvokeAsync()` with view rendering | Medium |
| **TempData persistence** | Persist TempData across redirects via OPFS | Medium |
| **Error pages** | Custom error views (404, 500) with developer exception page | Medium |

### Medium-Term

| Feature | Description | Priority |
|---------|-------------|----------|
| **NativeAOT / Trimming** | Reduce bundle size from ~15-25MB to <5MB | High |
| **Roslyn view compilation** | Optional compile-time `.cshtml` processing for full Razor coverage | Medium |
| **Streaming rendering** | `IAsyncEnumerable` support in views for progressive loading | Medium |
| **Differential DB sync** | Sync only changed records instead of full database snapshots | Medium |
| **F5 Debugging** | Visual Studio / VS Code debugger attach to WASM Worker | Medium |
| **Two-Factor Authentication** | TOTP-based 2FA in the Identity system | Low |

### Long-Term

| Feature | Description | Priority |
|---------|-------------|----------|
| **Blazor Interop** | Embed MVC views inside Blazor components and vice versa | Medium |
| **MAUI WebView** | Ship MVC apps inside MAUI native applications | Medium |
| **WebTransport bridge** | Cross-device SignalR via WebTransport/WebSocket relay | Low |
| **Docker hosting** | Containerized CephaKit API server | Low |
| **Native Cloudflare Workers** | First-class Cloudflare Workers deployment target | Low |
| **Service Worker caching** | PWA-style resource caching for instant re-loads | Low |

---

## .NET Integration Roadmap

If this project gains community traction and .NET team interest:

| Phase | Goal |
|-------|------|
| **Phase 1** (Current) | Community SDK — validate the model, gather feedback |
| **Phase 2** | Experimental SDK — `Microsoft.NET.Sdk.WebAssembly.Mvc` as an opt-in preview |
| **Phase 3** | Production readiness — NativeAOT, tooling, debugging, documentation |
| **Phase 4** | Ecosystem — Blazor interop, MAUI hosting, edge platform partnerships |

> See [docs/Proposal.md](Proposal.md) for the full .NET 11 feature proposal.

---

## Contributing

We welcome contributions in any of these areas. See [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

High-impact areas:
- NativeAOT / Trimming experimentation
- Additional Tag Helper implementations
- Model binding system
- Documentation improvements
- Test coverage
