# 🧬 NetWasmMvc.SDK

**The first complete ASP.NET MVC runtime for WebAssembly.**

Write MVC applications with Controllers, Views, SignalR Hubs, EF Core SQLite, and Identity — all running entirely in the browser. No server required.

> Powered by **Cepha** — inspired by *Physarum polycephalum*, the intelligent slime mold that solves complex problems through decentralized networks.

## ✨ Features

| Feature | Description |
|---------|-------------|
| 🎮 **MVC Controllers** | Full ASP.NET-style controllers with `[Route]` attributes |
| 📄 **Razor Views** | `.cshtml` templates with `@ViewBag`, `@Model`, `@foreach` |
| 📡 **SignalR Hubs** | Real-time communication (WebSocket-style) in WASM |
| 🗄️ **EF Core SQLite** | Full database with Entity Framework — in the browser |
| 🔐 **Identity** | User management, roles, authentication — client-side |
| 🌐 **SPA Router** | Automatic client-side navigation, history support |
| 🚀 **CephaKit** | Optional Node.js WASM backend server |

## 🚀 Quick Start

### 1. Create a new project

```xml
<Project Sdk="NetWasmMvc.SDK">
</Project>
```

### 2. Write your Program.cs

```csharp
var app = CephaApp.Create();
await app.RunAsync();
```

### 3. Add a Controller

```csharp
using WasmMvcRuntime.Abstractions;

public class HomeController : Controller
{
    [Route("/")]
    [Route("/home/index")]
    public ViewResult Index()
    {
        ViewBag["Title"] = "Hello, Cepha!";
        return View();
    }
}
```

### 4. Add a View

Create `Views/Home/Index.cshtml`:

```html
<h1>@ViewBag.Title</h1>
<p>Running in WebAssembly! 🧬</p>
```

### 5. Build & Run

```bash
dotnet build
# Serve wwwroot with any static file server
```

## 📦 What's Included

The SDK bundles everything — **zero additional PackageReferences needed**:

- `WasmMvcRuntime.Abstractions` — Base classes (Controller, Hub, Route, ViewResult)
- `WasmMvcRuntime.Core` — MVC Engine, SignalR Engine, View Rendering
- `WasmMvcRuntime.Identity` — User/Role management
- `WasmMvcRuntime.Data` — EF Core SQLite for WASM
- `WasmMvcRuntime.App` — Shared Controllers, Hubs, Models, Repositories
- **JsInterop** — `[JSImport]` bindings (DOM, storage, navigation)
- **JsExports** — `[JSExport]` handlers (Navigate, Forms, SignalR)
- **CephaApp** — One-call bootstrap builder
- **main.js** — Default SPA runtime (override with your own)
- **Deployment templates** — Local, Cloudflare Pages, Azure Static Web Apps

## 🏗️ Architecture

```
Browser
  └── WebAssembly (.NET 10)
       ├── MVC Engine (route → controller → view)
       ├── SignalR Engine (real-time hubs)
       ├── EF Core + SQLite (IndexedDB-backed)
       ├── Identity (auth & roles)
       └── JS Interop (DOM, navigation, storage)
```

## 🔗 Links

- **Repository**: [github.com/sbay-dev/WasmMvcRuntime](https://github.com/sbay-dev/WasmMvcRuntime)
- **License**: MIT

---

*Built with 🧬 by [sbay-dev](https://github.com/sbay-dev)*
