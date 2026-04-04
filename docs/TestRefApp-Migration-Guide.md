# TestRefApp → Cepha WASM Migration Guide

> **Zero source-code changes.** This guide documents how `TestRefApp` — a standard ASP.NET Core MVC application — was migrated to run entirely in the browser via WebAssembly using `NetWasmMvc.SDK`, without modifying a single line of application code.

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [What Changed (SDK Only)](#what-changed-sdk-only)
4. [Shim Architecture](#shim-architecture)
5. [Request Flow](#request-flow)
6. [Key Challenges & Solutions](#key-challenges--solutions)
7. [Build & Run](#build--run)
8. [Known Limitations](#known-limitations)

---

## Overview

**TestRefApp** is a NetContainer.Ref management console built with:
- ASP.NET Core MVC (`Controller`, `IActionResult`, `[HttpGet]`, `[HttpPost]`)
- Dependency Injection (`IRefOrchestratorService`)
- Razor Views (`_Layout.cshtml`, `@section`, `@RenderBody()`, tag helpers)
- API endpoints (REST JSON APIs at `/api/guests`, `/api/snapshots`, etc.)
- Server-Sent Events (`EventSource` for live boot logs)
- Background Services (`IHostedService` for auto-starting guests)
- Static assets (jQuery, Bootstrap, custom CSS/JS with `~/` prefix)

The **only change** to migrate was replacing the SDK reference in `.csproj`:

```xml
<Project Sdk="NetWasmMvc.SDK/1.0.53">
```

Everything else — controllers, views, models, Program.cs — remained untouched.

---

## Architecture

```
┌─────────────── Browser ───────────────────┐
│                                            │
│  index.html                                │
│    └─ main.js (Display Surface)            │
│         ├─ DOM rendering (innerHTML)       │
│         ├─ fetch() interceptor → Worker    │
│         ├─ EventSource interceptor         │
│         ├─ SPA router (link clicks)        │
│         └─ CSS/JS activation (~/→/)        │
│                                            │
│  Web Worker (cepha-runtime-worker.js)       │
│    └─ .NET WASM Runtime                    │
│         ├─ Program.cs → WebApplication     │
│         │   shim → CephaApp.Create()       │
│         ├─ MvcEngine (route discovery)     │
│         ├─ RazorTemplateEngine             │
│         ├─ HomeController (business logic) │
│         ├─ BrowserRefOrchestrator (stubs)  │
│         └─ GuestBootService (auto-start)   │
│                                            │
└────────────────────────────────────────────┘
```

**Key principle:** The main thread only renders HTML — all .NET logic runs in a Web Worker.

---

## What Changed (SDK Only)

### 1. MVC Type Shims (`MvcShims.cs`)

Provides `Microsoft.AspNetCore.Mvc` types that shadow the real ASP.NET Core types at compile time (CS0436):

| Shimmed Type | Purpose |
|---|---|
| `Controller` / `ControllerBase` | Base classes with `View()`, `Json()`, `Ok()`, `NotFound()`, `BadRequest()` |
| `IActionResult` | Covariant interface bridging SDK ↔ Abstractions |
| `JsonResult` | Serializes with **camelCase** (matching ASP.NET Core defaults) |
| `ViewResult` | Renders Razor templates via `RazorTemplateEngine` |
| `HttpGetAttribute` / `HttpPostAttribute` | Route template attributes implementing `IRouteTemplateProvider` |
| `FromBodyAttribute` | Marks parameters for JSON body deserialization |
| `HttpContext` / `HttpRequest` / `HttpResponse` | Minimal surfaces for `Response.StatusCode`, `Response.ContentType`, `Request.Headers` |

### 2. Hosting & DI Shims (`HostingShims.cs`, `WebApplicationShims.cs`)

| Shimmed Type | Behavior |
|---|---|
| `WebApplicationBuilder` | Creates `CephaServiceCollection`, returns `WebApplication` from `Build()` |
| `WebApplication` | `Run()` → `CephaApp.Create()` + starts hosted services + `RunAsync("/")` |
| `AddHostedService<T>()` | Registers service in DI + starts it after `CephaApp.Create()` |
| `BackgroundService` | Full implementation with `CancellationTokenSource` lifecycle |
| `ILogger<T>` | Registered as `NullLogger<T>` (no-op) via open generic |
| `IHostApplicationLifetime` | Stub interface |

### 3. NetContainer.Ref Shims (`NetContainerShims.cs`)

Browser-safe stubs for the entire NetContainer.Ref API:

| Shimmed Type | Behavior |
|---|---|
| `BrowserRefOrchestratorService` | In-memory guest list, snapshot creation/restore |
| `BrowserGuestContext` | Guest with `IsRunning=true`, stub ports, timestamps |
| `BrowserShellService` | Returns empty results |
| `BrowserLogStreamService` | `TailQemuLogAsync` yields nothing (no streaming data) |
| `BrowserAnalyticsService` | Returns stub metrics |
| `BrowserQemuAuditService` | No-op audit recording |

### 4. Static Assets & Routing Shims (`StaticAssetsShims.cs`)

| Shimmed Method | Behavior |
|---|---|
| `app.MapStaticAssets()` | No-op (static files served from `wwwroot/` by the host) |
| `app.MapControllerRoute(...)` | No-op (MvcEngine handles all routing) |
| `.WithStaticAssets()` | No-op fluent chain |
| `app.MapNetContainerTerminal(...)` | No-op (WebSocket terminals unsupported in WASM) |

### 5. CephaApp Bootstrap (`CephaApp.cs`)

- Registers `MvcEngine`, `SignalREngine`, user services, and `ILogger<>` fallback
- `HandleRequest`: routes through `MvcEngine.ProcessRequestAsync()` + `PostProcessHtml()`
- `PostProcessHtml()`: resolves `~/` paths, emulates `asp-controller`/`asp-action` tag helpers, strips `asp-append-version`

### 6. Razor Template Engine (`RazorTemplateEngine.cs`)

- **`@section` extraction**: Character-by-character brace-counting parser (handles unlimited nesting depth — needed for JavaScript with 3+ levels of nested braces)
- **`~/` path resolution**: `ResolveTildePaths()` converts `~/lib/...` → `/lib/...` in `href`, `src`, `action` attributes
- **Tag helper emulation**: `<a asp-controller="X" asp-action="Y">` → `<a href="/X/Y">`
- **`@RenderSectionAsync("Scripts", required: false)`** support

### 7. Browser Runtime (`main.js`)

| Feature | Implementation |
|---|---|
| **fetch interceptor** | Intercepts same-origin API paths → routes through Worker → MvcEngine |
| **EventSource interceptor** | Stubs SSE connections for API paths (no real streaming in WASM) |
| **`~/` resolution** | Resolves in `activateScripts()` CSS hrefs + JS srcs, and in `applyFrame()` |
| **Script reactivation** | `let/const` → `var` replacement (avoids redeclaration errors on SPA re-render while keeping functions global for `onclick` handlers) |
| **Service Worker** | Opt-in only via `window.__cephaServiceWorker = true` |
| **SPA routing** | Intercepts `<a>` clicks, calls `worker.postMessage({ type: 'navigate' })` |

### 8. SDK Build Targets (`Sdk.targets`)

- SDK runtime files (`main.js`, `cepha-runtime-worker.js`, `cepha-data-worker.js`) always override consumer project copies
- `_CephaSyncRuntimeFiles` target: copies SDK JS files to project `wwwroot/` on every build (for VS dev server compatibility)

---

## Request Flow

### Page Request: `GET /`

```
Browser → main.js loads → Worker boots .NET runtime
  → Program.Main() runs → WebApplication.Run()
    → CephaApp.Create() + start HostedServices
    → GuestBootService.ExecuteAsync() creates a BrowserGuestContext
    → CephaApp.RunAsync("/") navigates to "/"
      → HandleRequest("GET", "/", null, null)
        → MvcEngine.ProcessRequestAsync()
          → ResolveRoute("/") → "/home/index"
          → CreateController(HomeController)
            → DI injects BrowserRefOrchestratorService
          → Invoke Index() → ViewResult
          → RazorTemplateEngine.RenderWithLayoutAsync()
            → Renders Index.cshtml + _Layout.cshtml
            → Extracts @section Scripts
            → Resolves ~/ paths
        → PostProcessHtml() (asp-* tag helpers, ~/ cleanup)
      → Worker posts { type: 'frame', html } to main thread
  → main.js renders HTML into #app
  → activateScripts() promotes CSS/JS, resolves ~/, replaces let→var
  → Inline scripts run: refreshAll() → fetch('/api/guests')
```

### API Request: `GET /api/guests`

```
JS calls fetch('/api/guests')
  → fetch interceptor matches (starts with /, no file extension)
  → Posts { type: 'fetch', method: 'GET', path: '/api/guests' } to Worker
  → Worker calls HandleRequest('GET', '/api/guests', null, null)
    → MvcEngine resolves route → HomeController.ListGuests()
    → _orch.GetRunningGuests() → [BrowserGuestContext]
    → Json(guests) → JsonResult with camelCase serialization
  → Worker posts { type: 'fetch-result', response: '{"statusCode":200,...}' }
  → fetch interceptor creates Response object
  → JS receives JSON with { id, tenantId, arch, isRunning, ... }
```

### Parameterized API: `POST /api/guests/{id}/freeze`

```
JS calls fetch(`/api/guests/${id}/freeze`, { method: 'POST' })
  → fetch interceptor → Worker → HandleRequest
    → MvcEngine.ResolveRouteWithParams()
      → Template: /api/guests/{id}/freeze
      → Segment match: extracts id = "b01446..."
    → HomeController.Freeze(id, CancellationToken.None)
      → _orch.GetGuest(id) → BrowserGuestContext
      → ctx.FreezeAsync() → Task.CompletedTask (stub)
      → Json({ status: "frozen", guestId: id })
  → Response: {"status":"frozen","guestId":"b01446..."}
```

---

## Key Challenges & Solutions

### 1. `@section Scripts` with deeply nested JavaScript

**Problem:** The original regex-based `ExtractSections()` only handled 2 levels of `{}` nesting. JavaScript code like `function { try { fetch(${url}) } }` has 3+ levels, causing the section to be truncated.

**Solution:** Replaced regex with a character-by-character brace counter that tracks nesting depth and handles unlimited levels.

### 2. `~/` path prefix resolution

**Problem:** ASP.NET Core resolves `~/` to the app root at compile time via tag helpers. In WASM, tag helpers don't exist.

**Solution:** Triple-layer defense:
1. `RazorTemplateEngine.ResolveTildePaths()` — during Razor rendering
2. `CephaApp.PostProcessHtml()` — after rendering (safety net)
3. `main.js activateScripts()` — when promoting CSS/JS elements to live DOM

### 3. Inline script function scope

**Problem:** Wrapping inline `<script>` blocks in IIFE `(function(){...})()` prevents `let/const` redeclaration errors on SPA re-renders, but also makes functions like `refreshAll()` local — invisible to `onclick` handlers.

**Solution:** Replace `let/const` declarations with `var` (allows redeclaration, keeps functions global).

### 4. JSON property casing

**Problem:** `System.Text.Json` defaults to PascalCase (`Id`, `IsRunning`), but JavaScript expects camelCase (`id`, `isRunning`) — matching ASP.NET Core's default behavior.

**Solution:** Added `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` to all JSON serialization via shared `CephaJsonDefaults.Options`.

### 5. EventSource (SSE) bypass

**Problem:** `new EventSource('/api/guests/{id}/logs/stream')` doesn't use `fetch()` — it's a separate browser API that hits the real server, getting HTML instead of `text/event-stream`.

**Solution:** Intercepted the `EventSource` constructor. For API paths, returns a stub that immediately closes without errors.

### 6. Hosted services not starting

**Problem:** `AddHostedService<GuestBootService>()` was a no-op shim, so no guest was ever auto-created. The API returned empty arrays.

**Solution:** Made `AddHostedService` actually register the type in DI, and start all hosted services after `CephaApp.Create()`.

### 7. Missing ILogger<T> in DI

**Problem:** `GuestBootService` and controllers depend on `ILogger<T>`, which wasn't registered in the browser DI container.

**Solution:** Registered `ILogger<>` → `NullLogger<>` as an open generic in `CephaApp.Create()`.

---

## Build & Run

### Prerequisites

- .NET 10 Preview SDK
- `NetWasmMvc.SDK` NuGet package (v1.0.53+)

### Steps

```bash
# 1. Restore and build
cd samples/TestRefApp
dotnet restore
dotnet build -c Release

# 2. Publish (creates WASM bundle)
dotnet publish -c Release

# 3. Serve the published output
cd bin/Release/net10.0/publish/wwwroot
dotnet serve --port 5050 --mime .wasm=application/wasm --mime .js=text/javascript
```

Open `http://localhost:5050` in a Chromium browser.

### Visual Studio

The app also works directly from Visual Studio:
1. Open `TestRefApp.sln`
2. Press F5 (uses `https://localhost:7283` from `launchSettings.json`)
3. The `_CephaSyncRuntimeFiles` build target automatically copies SDK runtime files to `wwwroot/`

---

## Known Limitations

| Limitation | Reason |
|---|---|
| Serial console returns "No serial port" | `BrowserGuestContext.SerialPort` is 0 (no real QEMU in browser) |
| No live boot logs streaming | `BrowserLogStreamService.TailQemuLogAsync` yields nothing |
| Freeze/Resume are no-ops | Stub implementation — no actual VM state management |
| Snapshots are in-memory only | No real disk images in browser — stored in `List<SnapshotInfo>` |
| No WebSocket terminals | `MapNetContainerTerminal` is a no-op |
| Guest data resets on page reload | In-memory state — use CephaData/OPFS for persistence |

---

## File Structure

```
samples/TestRefApp/
├── Controllers/
│   └── HomeController.cs        # MVC controller with API endpoints (UNMODIFIED)
├── Models/
│   └── ErrorViewModel.cs        # Error view model (UNMODIFIED)
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml          # Main UI with JS business logic (UNMODIFIED)
│   │   └── Privacy.cshtml        # Privacy page (UNMODIFIED)
│   ├── Shared/
│   │   ├── _Layout.cshtml        # Layout with ~/ paths, @section (UNMODIFIED)
│   │   └── Error.cshtml          # Error view (UNMODIFIED)
│   ├── _ViewImports.cshtml       # Tag helper imports (UNMODIFIED)
│   └── _ViewStart.cshtml         # Layout assignment (UNMODIFIED)
├── wwwroot/
│   ├── main.js                   # SDK runtime (auto-synced by build)
│   ├── cepha-runtime-worker.js   # .NET WASM worker (auto-synced)
│   ├── cepha-data-worker.js      # OPFS data worker (auto-synced)
│   ├── index.html                # SPA shell (from SDK)
│   ├── css/site.css              # App styles (UNMODIFIED)
│   ├── js/site.js                # App scripts (UNMODIFIED)
│   └── lib/                      # Bootstrap, jQuery (UNMODIFIED)
├── Program.cs                    # Standard ASP.NET Core startup (UNMODIFIED)
├── TestRefApp.csproj             # Only change: Sdk="NetWasmMvc.SDK/1.0.53"
├── nuget.config                  # Points to local NuGet feed
└── global.json                   # .NET SDK version pin
```

**The only modified file is `TestRefApp.csproj`** — changing the SDK attribute.
