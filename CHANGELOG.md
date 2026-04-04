# Changelog

All notable changes to this project will be documented in this file.

## [1.0.53] — 2026-04-04

### Added
- **TestRefApp sample** (`samples/TestRefApp/`) — full ASP.NET Core MVC app running in browser via WASM with zero source-code changes
- **MVC type shims** (`shared/MvcShims.cs`) — Controller, ControllerBase, JsonResult, HTTP method attributes, HttpContext
- **Hosting shims** (`shared/HostingShims.cs`) — IHostedService, BackgroundService, ILogger<T>, NullLogger<T>
- **WebApplication shims** (`shared/WebApplicationShims.cs`) — WebApplicationBuilder, WebApplication, CephaServiceCollection with working `AddHostedService<T>()`
- **NetContainer.Ref shims** (`shared/NetContainerShims.cs`) — BrowserRefOrchestratorService, BrowserGuestContext, and all domain service stubs
- **Static assets shims** (`shared/StaticAssetsShims.cs`) — MapStaticAssets, MapControllerRoute, WithStaticAssets no-ops
- **Environment shims** (`shared/EnvironmentShims.cs`) — IWebHostEnvironment stub
- **EventSource interception** in `main.js` — stubs SSE connections for API paths silently
- **Service worker opt-in** — registration only when `window.__cephaServiceWorker = true`
- **Migration documentation** (`docs/TestRefApp-Migration-Guide.md`)

### Fixed
- **JSON camelCase serialization** — added `CephaJsonDefaults.Options` with `PropertyNamingPolicy.CamelCase` matching ASP.NET Core defaults; applied to `JsonResult`, `OkObjectResult`, `NotFoundObjectResult`, `ObjectResult`, and `MvcEngine` fallback
- **`@section` extraction** — replaced 2-level regex with character-by-character brace counter supporting unlimited nesting depth
- **`~/` path resolution** — triple-layer defense: RazorTemplateEngine, CephaApp.PostProcessHtml, main.js activateScripts
- **Inline script scoping** — `let/const` → `var` replacement in activateScripts() to keep functions global for onclick handlers while avoiding redeclaration errors
- **AddHostedService<T>** — was a no-op; now registers in DI and starts after CephaApp.Create()
- **ILogger<T> missing** — registered as open generic NullLogger<> in CephaApp.Create()
- **Startup hang** — added timeouts for OPFS/session restore stages (2-3s) and initial navigate (8s)
- **DevLog JSImport crash** — replaced cepha.isDevMode dependency with direct hostname check

### Changed
- SDK banner now shows dynamic version from NuGet package folder
- ASP.NET Core runtime assemblies bundled for browser-wasm target

## [1.0.6] — 2026-02-20

### Fixed
- SQLite native library crash on Android/Termux proot environments (resolves #9)
- `SqliteNativeResolver` now handles `DllNotFoundException` for `e_sqlite3` gracefully

### Changed
- SDK `Sdk.targets` updated with improved SQLite native resolution

## [1.0.5] — 2026-02-18

### Added
- Persistent interactive menu in Cepha.CLI with sub-menus and navigation
- Dynamic SDK version resolution in CLI
- CLI logo integration

### Changed
- CLI no longer exits after executing a single command — stays alive until explicit exit

## [1.0.4] — 2026-02-15

### Added
- `Cepha.Sdk` — internal MSBuild SDK for unified Client/Server builds
- Server mode (`CephaMode=Server`) for Node.js WASM hosting via CephaKit
- Deployment targets: Local, Cloudflare Pages, Azure Static Web Apps

### Changed
- `WasmMvcRuntime.Client` migrated from `Microsoft.NET.Sdk.BlazorWebAssembly` to `Cepha.Sdk`

## [1.0.3] — 2026-02-10

### Added
- SignalR in-process engine with reflection-based hub dispatch
- `SessionStorageService` with HMAC-signed tokens and fingerprint binding
- `IdentityAtom` — 128-byte binary cryptographic identity entity

### Changed
- Identity system upgraded to PBKDF2 (600,000 iterations) with account lockout

## [1.0.2] — 2026-02-05

### Added
- EF Core + SQLite running in WebAssembly with OPFS persistence
- Data Worker thread for isolated database operations
- `WasmMvcRuntime.Data` library with cloud provider abstractions

## [1.0.1] — 2026-01-28

### Added
- Razor template engine (regex-based `.cshtml` processing)
- Layout system with `_Layout.cshtml` support
- `ViewData` dictionary passing from controllers to views

## [1.0.0] — 2026-01-20

### Added
- Initial release of WasmMvcRuntime
- `Controller` base class with action method dispatch
- `MvcEngine` — URL-to-controller routing in WebAssembly
- `IActionResult` / `ViewResult` / `JsonResult` / `RedirectResult`
- `WasmHttpContext` for client-side HTTP context simulation
- `NetWasmMvc.SDK` published to NuGet
- `Cepha.CLI` published to NuGet
- Three-thread architecture: Main → Runtime Worker → Data Worker
