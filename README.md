<div align="center">

# NetWasmMVCRuntime

### Client-Side ASP.NET MVC Runtime for WebAssembly

[![NuGet SDK](https://img.shields.io/nuget/v/NetWasmMvc.SDK?label=SDK&logo=nuget&color=004880)](https://www.nuget.org/packages/NetWasmMvc.SDK)
[![NuGet CLI](https://img.shields.io/nuget/v/Cepha.CLI?label=CLI&logo=nuget&color=004880)](https://www.nuget.org/packages/Cepha.CLI)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Build](https://github.com/sbay-dev/NetWasmMVCRuntime/actions/workflows/build.yml/badge.svg)](https://github.com/sbay-dev/NetWasmMVCRuntime/actions/workflows/build.yml)

*Run Controllers, Razor Views, EF Core, Identity, and SignalR entirely in the browser — no server required.*

[Architecture](#architecture) · [SDK Design](#sdk-architecture) · [CLI Design](#cli-architecture) · [Documentation](docs/) · [.NET 11 Proposal](docs/Proposal.md)

</div>

---

## Why This Exists

ASP.NET Core today offers two client-side hosting models: **Blazor Server** (server-rendered, SignalR-connected) and **Blazor WebAssembly** (component-based SPA). Neither provides the **Controller → Action → View** pattern that millions of .NET developers know from MVC.

**NetWasmMVCRuntime** fills this gap. It is a reference implementation proving that the full MVC programming model — controllers, Razor views, model binding, action results — can execute entirely in WebAssembly, with no server dependency.

This repository serves as the technical foundation for a [formal .NET 11 proposal](docs/Proposal.md) to introduce client-side MVC as a complementary hosting model in ASP.NET Core.

## Overview

```xml
<Project Sdk="NetWasmMvc.SDK/1.0.6">
  <!-- Zero-config — Controllers, Views, EF Core, Identity, SignalR -->
  <!-- All bundled. No PackageReferences needed. -->
</Project>
```

A single `<Project Sdk="...">` line gives you a complete MVC application running in the browser. The SDK bundles the runtime, Razor engine, EF Core with SQLite, Identity, and SignalR — all pre-configured for `browser-wasm`.

## Key Capabilities

| Capability | What It Does |
|------------|-------------|
| **MVC Pipeline** | `Controller → Action → ViewResult → HTML` executing in a Web Worker |
| **Razor View Engine** | `.cshtml` template processing without Roslyn compilation overhead |
| **EF Core + SQLite** | Entity Framework Core with SQLite persisted to OPFS (Origin Private File System) |
| **Client-Side Identity** | PBKDF2 password hashing, HMAC-signed sessions, fingerprint binding, account lockout |
| **In-Process SignalR** | Hub dispatch via reflection — real-time messaging without network transport |
| **Dual Hosting** | Same codebase runs as Browser SPA or Node.js/Edge API server |
| **CLI Tooling** | `cepha` CLI for scaffolding, dev server, benchmarks, and deployment |

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                      Browser                             │
│                                                          │
│  ┌──────────────┐    ┌────────────────────────────────┐ │
│  │  Main Thread  │◄──►│  Runtime Worker                │ │
│  │  (UI / DOM)   │    │   ├─ MvcEngine                 │ │
│  │               │    │   ├─ RazorTemplateEngine        │ │
│  │  Renders HTML │    │   ├─ ControllerInvoker          │ │
│  │  returned by  │    │   ├─ SignalREngine              │ │
│  │  MVC pipeline │    │   └─ IdentityService            │ │
│  └──────────────┘    └──────────┬─────────────────────┘ │
│                                  │                       │
│                      ┌──────────▼─────────────────────┐ │
│                      │  Data Worker                    │ │
│                      │   ├─ EF Core DbContext          │ │
│                      │   ├─ SQLite (e_sqlite3.a)       │ │
│                      │   └─ OPFS Persistence Layer     │ │
│                      └────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### Thread Model

| Thread | Responsibility | Key Types |
|--------|---------------|-----------|
| **Main** | DOM rendering, user input, navigation interception | `JsInterop`, `JsExports` |
| **Runtime Worker** | MVC pipeline execution, controller dispatch, Razor rendering | `MvcEngine`, `RazorTemplateEngine`, `SignalREngine` |
| **Data Worker** | Database operations, OPFS file I/O | `ApplicationDbContext`, `IdentityDbContext` |

This three-thread architecture ensures the UI thread never blocks on MVC processing or database queries.

### Request Lifecycle

```
User clicks link
    → Main thread intercepts navigation (pushState)
    → Posts message to Runtime Worker
    → MvcEngine.ProcessRequest(url)
        → Resolves Controller + Action via reflection
        → Invokes action method
        → If ViewResult: RazorTemplateEngine.RenderView(viewName, viewData)
        → Returns rendered HTML string
    → Main thread receives HTML
    → Updates DOM via innerHTML
```

## SDK Architecture

The **NetWasmMvc.SDK** is an MSBuild SDK that transforms a standard .NET project into a self-contained WebAssembly MVC application. It is designed as a reference for how client-side MVC could be integrated into the official .NET SDK.

```
NetWasmMvc.SDK/
├── Sdk/
│   ├── Sdk.props          # Build configuration (TFM, runtime, WASM settings)
│   └── Sdk.targets        # Auto-imports, DLL bundling, deployment targets
├── lib/
│   └── net10.0/
│       └── WasmMvcRuntime.Core.dll   # Pre-compiled runtime (avoid recompilation)
├── shared/
│   ├── CephaApp.cs                   # Application bootstrap
│   ├── JsInterop.cs                  # Browser ↔ .NET bridge
│   ├── JsExports.cs                  # Exported methods for JS
│   └── SqliteNativeResolver.cs       # SQLite native library resolver
├── content/
│   └── wwwroot/
│       ├── index.html                # SPA shell
│       ├── main.js                   # Bootstrap + navigation
│       ├── cepha-runtime-worker.js   # MVC pipeline worker
│       └── cepha-data-worker.js      # Database worker
└── deploy/
    ├── local/                        # Local dev server
    ├── cloudflare/                   # Cloudflare Pages config
    └── azure/                        # Azure Static Web Apps config
```

### SDK Design Principles

1. **Zero-Config**: A single `<Project Sdk="NetWasmMvc.SDK/1.0.6">` line is enough. No `PackageReference` nodes needed.
2. **Convention over Configuration**: Controllers in `Controllers/`, Views in `Views/{ControllerName}/`, Models in `Models/`.
3. **Pre-compiled Runtime**: The SDK bundles pre-compiled DLLs to avoid recompiling the runtime on every build.
4. **Deployment Targets**: Built-in MSBuild targets for local, Cloudflare Pages, and Azure Static Web Apps deployment.
5. **Extensible**: Developers can override any default by setting MSBuild properties in their `.csproj`.

### How Sdk.props Works

```xml
<!-- Simplified view of what Sdk.props configures -->
<Import Sdk="Microsoft.NET.Sdk.WebAssembly" />
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PublishTrimmed>false</PublishTrimmed>
</PropertyGroup>
```

### How Sdk.targets Works

```xml
<!-- Simplified view of what Sdk.targets provides -->
<!-- 1. Auto-import runtime DLLs -->
<Reference Include="WasmMvcRuntime.Abstractions" HintPath="$(SdkPath)/lib/..." />
<Reference Include="WasmMvcRuntime.Core" HintPath="$(SdkPath)/lib/..." />

<!-- 2. Embed .cshtml files as resources -->
<EmbeddedResource Include="Views/**/*.cshtml" />

<!-- 3. Include SQLite native for browser-wasm -->
<NativeFileReference Include="e_sqlite3.a" />

<!-- 4. Auto-generate global usings -->
<Using Include="WasmMvcRuntime.Abstractions" />
<Using Include="WasmMvcRuntime.Core" />
```

## CLI Architecture

The **Cepha CLI** (`cepha`) is a .NET global tool for scaffolding, running, and deploying NetWasmMVC applications. It is designed as a reference for how `dotnet new wasmmvc` could work in the official .NET CLI.

```
Cepha.CLI/
├── Program.cs              # Entry point, interactive menu
├── Commands/
│   ├── NewCommand.cs       # Project scaffolding
│   ├── RunCommand.cs       # Dev server launcher
│   ├── BuildCommand.cs     # Build orchestration
│   ├── DeployCommand.cs    # Deployment automation
│   └── BenchmarkCommand.cs # Framework comparison benchmarks
├── Services/
│   ├── SdkResolver.cs      # Dynamic SDK version discovery
│   ├── TemplateEngine.cs   # Project template rendering
│   └── ServerManager.cs    # Dev server lifecycle
├── Templates/
│   ├── default/            # Standard MVC template
│   └── identity/           # MVC + Identity template
└── UI/
    └── ConsoleUI.cs        # Interactive menu system
```

### CLI Commands

```bash
# Install
dotnet tool install --global Cepha.CLI

# Scaffold a new project
cepha new my-app                    # Default MVC template
cepha new my-app --template identity # MVC + Identity template

# Development
cepha run                           # Start dev server with hot reload
cepha build                         # Build for production

# Deployment
cepha deploy local                  # Serve locally
cepha deploy cloudflare             # Deploy to Cloudflare Pages
cepha deploy azure                  # Deploy to Azure Static Web Apps

# Benchmarks
cepha benchmark                     # Compare Cepha vs React vs Vue vs Vanilla
```

### Template Scaffolding Flow

```
cepha new my-app
    → Resolves latest SDK version from NuGet
    → Creates project directory
    → Generates .csproj with Sdk="NetWasmMvc.SDK/{version}"
    → Scaffolds Controllers/HomeController.cs
    → Scaffolds Views/Home/Index.cshtml
    → Scaffolds Program.cs (entry point)
    → Runs dotnet restore
    → Ready to build and run
```

## Runtime Libraries

| Library | Purpose | Key Types |
|---------|---------|-----------|
| `WasmMvcRuntime.Abstractions` | Contracts and base classes | `Controller`, `IActionResult`, `ViewResult`, `WasmHttpContext` |
| `WasmMvcRuntime.Core` | Engine implementations | `MvcEngine`, `RazorTemplateEngine`, `SignalREngine` |
| `WasmMvcRuntime.Identity` | Authentication & authorization | `UserManager`, `SignInManager`, `IdentityAtom`, `SessionStorageService` |
| `WasmMvcRuntime.Data` | Data access layer | `ApplicationDbContext`, `BackupService`, Cloud providers |
| `WasmMvcRuntime.App` | Reference application | Controllers, Models, Hubs, Repositories |
| `WasmMvcRuntime.Client` | Browser host (SPA) | Entry point, JS interop, service registration |
| `WasmMvcRuntime.Cepha` | Node.js host (Server) | CephaServer, SSE, CephaKit integration |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### Quick Start with CLI

```bash
dotnet tool install --global Cepha.CLI
cepha new my-app
cd my-app
cepha run
```

### Quick Start with SDK

```xml
<!-- my-app.csproj -->
<Project Sdk="NetWasmMvc.SDK/1.0.6">
</Project>
```

```csharp
// Controllers/HomeController.cs
using WasmMvcRuntime.Abstractions;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Home";
        ViewData["Message"] = "Running in WebAssembly!";
        return View();
    }
}
```

```bash
dotnet build && dotnet run
```

### Building from Source

```bash
git clone https://github.com/sbay-dev/NetWasmMVCRuntime.git
cd NetWasmMVCRuntime
dotnet restore
dotnet build
```

> **Note:** `WasmMvcRuntime.Client` and `WasmMvcRuntime.Cepha` use `Cepha.Sdk` as their MSBuild SDK. The required package is in `local-packages/` and resolved via `nuget.config`.

## Project Structure

```
NetWasmMVCRuntime/
├── WasmMvcRuntime.Abstractions/   # Contracts: Controller, IActionResult, ViewResult
├── WasmMvcRuntime.Core/           # Engines: MvcEngine, Razor, SignalR
├── WasmMvcRuntime.Identity/       # Identity: UserManager, PBKDF2, HMAC sessions
├── WasmMvcRuntime.Data/           # Data: EF Core, SQLite WASM, cloud providers
├── WasmMvcRuntime.App/            # Reference app: controllers, models, hubs
├── WasmMvcRuntime.Client/         # Browser host (Blazor WASM entry point)
├── WasmMvcRuntime.Cepha/          # Node.js host (CephaKit server entry point)
├── NetWasmMvc.SDK/                # MSBuild SDK — zero-config project template
├── Cepha.Sdk/                     # Internal build SDK
├── Cepha.CLI/                     # CLI tool for scaffolding and deployment
├── samples/                       # Sample applications
├── docs/                          # Architecture, security, proposal, landing page
└── local-packages/                # Pre-built SDK NuGet packages for local build
```

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/Architecture.md) | Runtime architecture, thread model, rendering pipeline |
| [Hosting Models](docs/HostingModels.md) | Browser SPA, Node.js, Edge Workers, Embedded WebView |
| [Security Model](docs/SecurityModel.md) | Identity system, cryptographic primitives, threat model |
| [CLI Reference](docs/CLI.md) | Cepha CLI commands and workflows |
| [Roadmap](docs/Roadmap.md) | Current state and planned features |
| [.NET 11 Proposal](docs/Proposal.md) | Formal proposal for ASP.NET Core integration |

## .NET 11 Proposal

This project proposes adding **client-side MVC** as an experimental hosting model in .NET 11, complementing Blazor rather than replacing it.

### What We Are Asking

| Ask | Description |
|-----|-------------|
| **Recognize** | Client-side MVC as a valid WebAssembly hosting model |
| **Template** | `dotnet new wasmmvc` project template |
| **Package** | `Microsoft.AspNetCore.Mvc.WebAssembly` experimental package |
| **Razor Target** | Enable Razor view compilation targeting `browser-wasm` |
| **SDK Integration** | Evaluate `NetWasmMvc.SDK` patterns for official SDK |

### How This Complements Blazor

| Scenario | Blazor | Client-Side MVC |
|----------|--------|-----------------|
| Component-based UI | ✅ Primary model | ❌ Not the goal |
| Page-based navigation | ⚠️ Possible | ✅ Native pattern |
| Server-rendered feel | ❌ Different model | ✅ Controller → View |
| Existing MVC migration | ❌ Requires rewrite | ✅ Familiar pattern |
| Offline-first apps | ⚠️ Limited | ✅ Full OPFS persistence |

📄 **[Read the full proposal →](docs/Proposal.md)**

## .NET Foundation Readiness

This project is designed to meet .NET Foundation membership criteria:

- ✅ **MIT License** — permissive, foundation-compatible
- ✅ **CI/CD Pipeline** — GitHub Actions for build, test, publish
- ✅ **NuGet Packages** — SDK and CLI published to nuget.org
- ✅ **Comprehensive Documentation** — architecture, security model, hosting models, roadmap
- ✅ **Security Model** — documented threat model, cryptographic primitives, session management
- ✅ **Code of Conduct** — contributor guidelines in CONTRIBUTING.md
- ✅ **Semantic Versioning** — CHANGELOG.md following Keep a Changelog format
- ✅ **Cross-Platform** — targets `browser-wasm`, runs on Windows/Linux/macOS

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

MIT License — see [LICENSE](LICENSE) for details.

---

<div align="center">

**NetWasmMVCRuntime** — *Bringing the full ASP.NET MVC experience to WebAssembly.*

A reference implementation for client-side MVC in .NET · [sbay-dev](https://github.com/sbay-dev)

</div>

