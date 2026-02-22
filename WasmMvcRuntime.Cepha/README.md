# ?? WasmMvcRuntime.Cepha

**Cepha** — named after *Physarum polycephalum*, the remarkably intelligent slime mold — is the **headless back-end companion server** for [WasmMvcRuntime.Client](../WasmMvcRuntime.Client). Just as *Physarum polycephalum* builds decentralized networks that solve complex routing problems without a central brain, Cepha operates as a decentralized server node that carries the full logic of an ASP.NET Core back-end, deployable to **Azure App Service**, **Azure Functions**, **Cloudflare Workers**, or any edge-compatible runtime.

Cepha is the **server-side kit** that powers **Server-Sent Events (SSE)** streams for controller responses and **real-time bidirectional communication** through the built-in **SignalR engine** — all while running .NET logic compiled to WebAssembly.

---

## ?? Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Key Capabilities](#key-capabilities)
- [How It Works with WasmMvcRuntime.Client](#how-it-works-with-wasmmvcruntimeclient)
- [SignalR Real-Time Engine](#signalr-real-time-engine)
- [SSE Controller Support](#sse-controller-support)
- [Deployment Targets](#deployment-targets)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [License](#license)

---

## Overview

WasmMvcRuntime is a framework that brings the **ASP.NET Core MVC programming model** — controllers, action results, routing, filters, views, model binding, Identity, Entity Framework Core — directly into **WebAssembly**. The ecosystem is split into two complementary halves:

| Component | Role |
|---|---|
| **WasmMvcRuntime.Client** | Runs in the **browser** via Blazor WebAssembly SDK. Hosts MVC controllers, Razor views (as embedded resources), EF Core with SQLite, and the SignalR engine — all client-side. |
| **WasmMvcRuntime.Cepha** | Runs on the **server/edge**. Acts as the back-end counterpart — a lightweight .NET WebAssembly host that carries the same controller logic, SignalR hubs, data layer, and Identity pipeline, but executes on a server node or edge worker. |

Cepha bridges the gap between a fully client-side WASM app and the need for a traditional back-end, without requiring you to rewrite any of your controllers, hubs, or services.

---

## Architecture

```
???????????????????????????????????????????????????????????
?                      Browser                            ?
?  ?????????????????????????????????????????????????????  ?
?  ?          WasmMvcRuntime.Client (WASM)             ?  ?
?  ?  ??????????? ???????????? ?????????????????????  ?  ?
?  ?  ? MVC     ? ? SignalR   ? ? EF Core + SQLite  ?  ?  ?
?  ?  ? Engine  ? ? Engine    ? ? (browser-wasm)    ?  ?  ?
?  ?  ??????????? ???????????? ?????????????????????  ?  ?
?  ????????????????????????????????????????????????????  ?
?          ?  SSE / WS ?                                  ?
???????????????????????????????????????????????????????????
           ?           ?
     ??????????????????????????????????????????????????
     ?         WasmMvcRuntime.Cepha (Server)           ?
     ?  ??????????? ???????????? ??????????????????   ?
     ?  ? MVC     ? ? SignalR   ? ? Identity +     ?   ?
     ?  ? Engine  ? ? Hub Host  ? ? Data Layer     ?   ?
     ?  ??????????? ???????????? ??????????????????   ?
     ?                                                  ?
     ?  Deployable to:                                  ?
     ?  • Azure App Service / Azure Functions            ?
     ?  • Cloudflare Workers                             ?
     ?  • Any Node.js / WASI-compatible host             ?
     ????????????????????????????????????????????????????
```

---

## Key Capabilities

### ?? Full ASP.NET Core MVC Pipeline on the Edge

Cepha hosts the complete **MvcEngine** from `WasmMvcRuntime.Core`, which provides:

- **Convention-based and attribute routing** — `[Route]`, `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpDelete]`
- **Controller lifecycle** — automatic scanning, DI-based instantiation, action filters (`IActionFilter`, `IAsyncActionFilter`)
- **Action results** — `ViewResult`, `JsonResult`, `OkObjectResult`, `ContentResult`, `RedirectResult`, `StatusCodeResult`, and more
- **Model binding** — query strings, form data, JSON body binding
- **API controllers** — `[ApiController]` attribute for JSON-first endpoints
- **Razor view rendering** — embedded `.cshtml` templates compiled and rendered through the custom `RazorTemplateEngine` and `ViewRenderer`

### ?? ASP.NET Core Identity (WASM-native)

Powered by `WasmMvcRuntime.Identity`, Cepha supports:

- **UserManager** and **RoleManager** — user registration, role assignment, claims management
- **SignInManager** — cookie-less authentication flows suitable for WASM
- **PasswordHasher** — secure password hashing
- **SessionStorageService** — session persistence backed by browser or server-side storage
- **IdentityDbContext** — EF Core-backed identity store

### ?? Entity Framework Core with SQLite

Through `WasmMvcRuntime.Data`, Cepha includes:

- **EF Core SQLite** running on the `browser-wasm` runtime (via `e_sqlite3` native reference)
- **IDataProvider** abstraction for swappable data backends
- **BackupService** with cloud provider support (e.g., OneDrive)
- **Full migration and query tracking** support

### ?? Real-Time SignalR Engine

The `SignalREngine` from `WasmMvcRuntime.Core` provides a complete hub-based real-time communication layer:

- **Hub lifecycle** — `OnConnectedAsync`, `OnDisconnectedAsync`, automatic connection tracking
- **Client proxies** — `Clients.All`, `Clients.Caller`, `Clients.Others`, `Clients.Client(id)`, `Clients.Group(name)`
- **Group management** — `AddToGroupAsync`, `RemoveFromGroupAsync`
- **Method invocation** — reflection-based hub method dispatch with JSON argument parsing
- **DI-aware hub creation** — hubs are instantiated through the service provider
- **JS interop dispatch** — hub events are forwarded to the JavaScript host via `OnClientEvent` callback

### ?? Server-Sent Events (SSE) for Controllers

Cepha acts as an SSE relay, enabling:

- **Streaming controller responses** to the browser over persistent HTTP connections
- **Progressive rendering** — partial view updates delivered incrementally
- **Event-driven patterns** — controllers can push data to clients as events occur on the server
- The SSE channel complements SignalR's WebSocket transport, giving you two real-time primitives: **unidirectional (SSE)** and **bidirectional (SignalR)**

### ?? JS Interop Bridge

Cepha exposes its .NET logic to JavaScript via `[JSExport]` and `[JSImport]` attributes:

- **Navigate** — trigger MVC routing from JS
- **FormSubmit** — post form data to controllers
- **FetchRoute** — request a route and receive the rendered response
- **Hub operations** — connect, disconnect, and invoke hub methods from the JS host

---

## How It Works with WasmMvcRuntime.Client

`WasmMvcRuntime.Client` runs **entirely in the browser**, executing controllers, rendering views, and persisting data in client-side SQLite. It is a fully self-contained MVC application inside WebAssembly.

**Cepha** mirrors this architecture on the server side:

1. **Shared controllers** — The same `Controller` base class and action patterns used in the client are reused on the server. Write your controller once; it runs on both sides.
2. **Shared SignalR hubs** — Hubs like `ChatHub` work identically whether dispatched from the client's in-browser engine or from Cepha's server-side engine.
3. **Shared data models and Identity** — `ApplicationDbContext`, `IdentityDbContext`, repositories, and services are referenced by both projects.
4. **SSE as a synchronization channel** — Cepha streams controller output back to the client via SSE, enabling the client to offload heavy or privileged operations to the server while maintaining the same MVC contract.
5. **SignalR as the real-time channel** — For bidirectional communication (chat, notifications, live dashboards), Cepha hosts the hub and relays events to all connected clients.

This dual-runtime approach means:

- **Offline-first**: The client works entirely offline with its own SQLite and MVC engine.
- **Online-enhanced**: When Cepha is reachable, the client can delegate to the server for authoritative data, authentication, and real-time features.
- **Isomorphic .NET**: A single codebase targets both browser and server, with no API translation layer.

---

## SignalR Real-Time Engine

Cepha's SignalR engine provides the following workflow:

```
Client (Browser)                        Cepha (Server)
     ?                                       ?
     ????? Connect("Chat") ??????????????????
     ????? connectionId: "a1b2c3d4" ????????
     ?                                       ?
     ????? Invoke("Chat","SendMessage",      ?
     ?      "Alice","Hello!") ???????????????
     ?                                       ?
     ????? ReceiveMessage("Alice",           ?
     ?      "Hello!","14:30:00") ????????????
     ?                                       ?
     ????? Invoke("Chat","JoinGroup",        ?
     ?      "devs") ?????????????????????????
     ????? SystemMessage("a1b2c3d4          ?
     ?      joined group 'devs'") ???????????
```

Hubs are automatically discovered via assembly scanning. Any class inheriting from `WasmMvcRuntime.Abstractions.SignalR.Hub` is registered and routable.

---

## SSE Controller Support

Server-Sent Events allow Cepha to push incremental updates to the client:

```
Client                                  Cepha
  ?                                       ?
  ????? GET /weather/stream ??????????????
  ?                                       ?
  ????? event: data                       ?
  ?     data: {"temp":22,"city":"NYC"}    ?
  ?                                       ?
  ????? event: data                       ?
  ?     data: {"temp":18,"city":"LON"}    ?
  ?                                       ?
  ????? event: complete                   ?
  ?     data: done                        ?
```

Controllers return SSE-compatible responses, and the client's JavaScript bridge consumes the event stream, forwarding parsed payloads to the in-browser MVC engine for rendering.

---

## Deployment Targets

### Azure App Service

Deploy Cepha as a standard .NET web application to Azure App Service. The WASM runtime executes server-side, backed by Azure's managed infrastructure.

```bash
dotnet publish -c Release
az webapp deploy --resource-group <rg> --name <app> --src-path bin/Release/net10.0/publish
```

### Azure Functions

Package Cepha as an Azure Function triggered by HTTP requests. Ideal for pay-per-execution serverless scenarios.

### Cloudflare Workers

Cepha's `browser-wasm` target and Node.js-compatible entry point (`main.mjs`) make it directly deployable to **Cloudflare Workers**. The WASM module runs at the edge with sub-millisecond cold starts.

```bash
wrangler deploy
```

### Node.js (Self-hosted)

Run Cepha directly via Node.js for local development or custom hosting:

```bash
dotnet build -c Release
node bin/Release/net10.0/browser-wasm/AppBundle/main.mjs
```

---

## Project Structure

```
WasmMvcRuntime (Solution)
?
??? WasmMvcRuntime.Abstractions     # Shared contracts: Controller, Hub, IActionResult, HttpContext
?   ??? Controller.cs               # MVC Controller base class with ViewData, TempData, ViewBag
?   ??? ControllerBase.cs           # Minimal controller base (action results, model state)
?   ??? Attributes.cs               # [Route], [HttpGet], [ApiController], etc.
?   ??? ActionResults.cs            # JsonResult, OkObjectResult, ContentResult, etc.
?   ??? SignalR/Hub.cs              # SignalR Hub base, IHubCallerClients, IGroupManager
?   ??? Views/                      # RazorTemplateEngine, ViewRenderer, ViewLocator
?
??? WasmMvcRuntime.Core             # Runtime engines
?   ??? MvcEngine.cs                # Controller scanning, routing, request pipeline
?   ??? SignalREngine.cs            # Hub discovery, connection management, method dispatch
?
??? WasmMvcRuntime.Data             # Data access layer
?   ??? Providers/SQLiteDataProvider.cs
?   ??? Services/BackupService.cs
?   ??? CloudProviders/OneDriveProvider.cs
?
??? WasmMvcRuntime.Identity         # ASP.NET Core Identity for WASM
?   ??? Services/UserManager.cs
?   ??? Services/SignInManager.cs
?   ??? Services/RoleManager.cs
?   ??? Data/IdentityDbContext.cs
?
??? WasmMvcRuntime.Client           # Browser-side WASM MVC app
?   ??? Controllers/                # HomeController, WeatherController, ChatController, etc.
?   ??? Hubs/ChatHub.cs             # SignalR hub (runs in browser)
?   ??? Views/                      # Embedded .cshtml Razor templates
?   ??? Program.cs                  # DI setup, MVC + SignalR engine wiring
?   ??? JsExports.cs               # [JSExport] bridge to JavaScript
?
??? WasmMvcRuntime.Cepha            # ? YOU ARE HERE — Server-side WASM host
    ??? Program.cs                  # Entry point (Node.js / edge runtime)
    ??? Properties/AssemblyInfo.cs  # [SupportedOSPlatform("browser")]
    ??? WasmMvcRuntime.Cepha.csproj # SDK: Microsoft.NET.Sdk, TFM: net10.0, RID: browser-wasm
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/) (for local execution)
- [wrangler CLI](https://developers.cloudflare.com/workers/wrangler/) (optional, for Cloudflare Workers)

### Build

```bash
dotnet build -c Release
```

### Run Locally (Node.js)

```bash
node bin/Release/net10.0/browser-wasm/AppBundle/main.mjs
```

### Run from Visual Studio

Open the solution in Visual Studio 2022+ and set `WasmMvcRuntime.Cepha` as the startup project, then press **F5**.

---

## Configuration

Cepha shares the same `ServiceCollection`-based DI pattern as `WasmMvcRuntime.Client`:

```csharp
var services = new ServiceCollection();

// MVC Engine
services.AddSingleton<IMvcEngine, MvcEngine>();

// SignalR Engine
services.AddSingleton<ISignalREngine>(sp =>
{
    var engine = new SignalREngine(sp);
    engine.OnClientEvent = (hubName, method, connectionId, argsJson) =>
    {
        // Dispatch to connected clients via SSE or WebSocket
    };
    return engine;
});

// EF Core + SQLite
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=cepha.db"));

// Identity
services.AddScoped<IUserManager, UserManager>();
services.AddScoped<ISignInManager, SignInManager>();

var provider = services.BuildServiceProvider();
```

---

## Why "Cepha"?

*Physarum polycephalum* — the "many-headed slime mold" — is a single-celled organism that can:

- **Solve shortest-path problems** by building efficient transport networks
- **Adapt to changing environments** by rewiring its network topology
- **Operate without a central brain**, relying on distributed chemical signaling

Cepha embodies these principles:

- **Distributed** — runs at the edge, close to users, without a monolithic central server
- **Adaptive** — the same .NET code adapts to browser, server, or edge execution contexts
- **Efficient** — minimal overhead, WebAssembly-native, cold starts measured in milliseconds
- **Networked** — connects clients through SignalR hubs and SSE streams, forming a real-time communication mesh

---

## License

This project is part of the [WasmMvcRuntime](https://github.com/sbay-dev/WasmMvcRuntime) framework.