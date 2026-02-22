# 🧬 Cepha CLI

**The command-line toolkit for building, testing, and deploying ASP.NET MVC applications that run entirely in WebAssembly.**

[![NuGet](https://img.shields.io/nuget/v/Cepha.CLI?color=667eea&label=NuGet&logo=nuget)](https://www.nuget.org/packages/Cepha.CLI)
[![Downloads](https://img.shields.io/nuget/dt/Cepha.CLI?color=3fb950&label=Downloads)](https://www.nuget.org/packages/Cepha.CLI)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4?logo=dotnet)
![MIT License](https://img.shields.io/badge/License-MIT-yellow)

---

## What is Cepha?

Cepha is a pioneering SDK (**NetWasmMvc.SDK**) that runs the full ASP.NET MVC pipeline — controllers, Razor views, routing, model binding, and Identity — **entirely inside the browser** via WebAssembly.

| Layer | Technology |
|-------|-----------|
| **Runtime** | .NET 10 WASM running in a dedicated Web Worker |
| **Rendering** | Razor `.cshtml` views compiled and executed client-side |
| **Main Thread** | Zero .NET code — only a thin display surface (`main.js`) |
| **Persistence** | SQLite via OPFS (Origin Private File System) |
| **Networking** | SignalR hubs, CephaKit edge workers |

**Cepha CLI** (`cepha`) is the official command-line tool that scaffolds, develops, benchmarks, and deploys Cepha applications.

---

## Installation

```bash
dotnet tool install --global Cepha.CLI
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

After installation, verify:

```bash
cepha --version
```

---

## Quick Start

```bash
# Create a new project
cepha new my-app

# Start the development server
cd my-app
cepha dev

# Open https://localhost:5001 in your browser
```

The entire application — controllers, views, routing — runs in a Web Worker. The main thread only renders HTML.

---

## Commands

### `cepha new <name>` — Scaffold a new project

Creates a production-ready Cepha MVC application with the full project structure.

```bash
cepha new my-app              # Standard MVC app
cepha new my-app --identity   # With ASP.NET Identity (login, registration, roles)
cepha new my-app --benchmark  # With performance benchmark suite
```

**Generated structure:**

```
my-app/
├── Controllers/
│   └── HomeController.cs
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml
│   │   └── Privacy.cshtml
│   └── Shared/
│       └── _Layout.cshtml
├── wwwroot/
│   ├── css/cepha.css          # Material-inspired design system
│   ├── main.js                # Display surface (main thread)
│   ├── cepha-runtime-worker.js
│   └── service-worker.js
├── Program.cs
└── my-app.csproj
```

### `cepha dev` — Start the development server

Builds and launches the WASM application host with live output.

```bash
cepha dev
```

### `cepha kit` — Start the CephaKit backend server

Launches a backend runtime server for hybrid scenarios (client WASM + server API).

```bash
cepha kit                # Node.js mode (default, port 3001)
cepha kit --port 4000    # Custom port
cepha kit --wrangler     # Cloudflare Wrangler mode
```

CephaKit provides:
- HTTPS development certificates (auto-exported)
- Backend API endpoints alongside the WASM frontend
- Hot-reload compatible architecture

### `cepha publish` — Build and deploy for production

Publishes the application with Brotli pre-compression and multiple deployment targets.

```bash
cepha publish                  # Interactive target selection
cepha publish --folder         # Local folder output
cepha publish --cloudflare     # Cloudflare Pages deployment
cepha publish --azure          # Azure Static Web Apps
cepha publish --kit            # Cloudflare Pages + CephaKit Edge Worker
```

**Cloudflare deployment features:**
- Automatic Wrangler authentication (OAuth browser flow)
- Brotli pre-compression reporting
- Custom domain connection via Cloudflare API (DNS + SSL)
- CephaKit Edge Worker with SPA routing, CORS, WASM MIME types, and immutable caching

### `cepha benchmark` — Run performance stress tests

Launches an automated benchmark suite that stress-tests the WASM runtime against React, Vue, and Vanilla JS.

```bash
cepha benchmark
```

**8 stress tests** across **4 frameworks** (Cepha, React 18, Vue 3, Vanilla JS):

| Test | What it measures |
|------|-----------------|
| 🔥 **Animation Storm** | 500 spring-physics DOM nodes with mitosis splitting |
| 🎬 **DOM Flood** | Raw frame throughput — thousands of DOM writes/sec |
| 🎯 **Click Storm** | Moving targets — event latency under sustained fire |
| 🌌 **Particle Physics** | N-body gravity simulation, 5000 particles |
| 💎 **WebGL Forge** | 100K vertices, GPU saturation test |
| 🗄️ **Data Siege** | Millions of objects — sort, search, transform |
| 🔐 **Crypto Matryoshka** | Nested AES-GCM + SHA-256 deep chain |
| 🕳️ **Tunnel Breach** | ALL tests simultaneously |

Features:
- **🤖 Auto-Pilot** — Runs all tests sequentially with automatic scoring
- **⚔️ Framework Battle** — Runs every test across all 4 frameworks, displays comparison table with winner announcement
- **📊 Compare All** — Side-by-side score comparison with bar charts

### `cepha info` — Display project information

```bash
cepha info
```

Shows SDK version, Identity status, CephaKit status, controller count, view count, and build state.

### `cepha help` — Show usage documentation

```bash
cepha help
```

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│                    Browser                       │
│                                                  │
│  ┌──────────────┐    postMessage    ┌──────────┐│
│  │  Main Thread  │◄────────────────►│Web Worker ││
│  │              │                   │           ││
│  │  main.js     │   DOM frames     │ .NET 10   ││
│  │  (display    │◄─────────────────│ MVC       ││
│  │   surface)   │                   │ Runtime   ││
│  │              │   user events    │           ││
│  │  Renders     │─────────────────►│Controllers││
│  │  HTML only   │                   │Views      ││
│  │              │                   │Routing    ││
│  └──────────────┘                   │SQLite     ││
│                                     │Identity   ││
│                                     └──────────┘│
└─────────────────────────────────────────────────┘
```

**Key design principles:**

1. **Worker Sovereignty** — The .NET runtime runs exclusively in a Web Worker, never blocking the UI thread.
2. **Zero JS Framework** — The main thread is a thin display surface (~500 lines of vanilla JS). No React, no Vue, no Angular.
3. **Real MVC** — Controllers, model binding, Razor views, `ViewBag`, layouts, partial views — the full ASP.NET MVC programming model.
4. **Offline-First** — SQLite via OPFS provides persistent storage. Service worker enables full offline operation.

---

## Deployment Targets

| Target | Command | Features |
|--------|---------|----------|
| **Local Folder** | `cepha publish --folder` | Static files ready for any hosting provider |
| **Cloudflare Pages** | `cepha publish --cloudflare` | OAuth login, auto-deploy, custom domains |
| **Cloudflare + CephaKit** | `cepha publish --kit` | Edge Worker with SPA routing, CORS, WASM headers |
| **Azure Static Web Apps** | `cepha publish --azure` | SWA configuration with navigation fallback |

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Node.js 18+](https://nodejs.org/) (optional, for CephaKit backend)
- [Wrangler CLI](https://developers.cloudflare.com/workers/wrangler/) (optional, for Cloudflare deployment)

---

## Comparison with Traditional Approaches

| Feature | Cepha | Blazor WASM | React SPA |
|---------|-------|-------------|-----------|
| Runtime location | Web Worker | Main thread | Main thread |
| UI thread blocking | Never | Possible | Possible |
| Programming model | MVC (Controllers + Razor) | Components | Components |
| Server required | No | No | No |
| Offline storage | SQLite (OPFS) | localStorage | localStorage |
| Bundle size (Hello World) | ~9 MB (Brotli) | ~5 MB | ~200 KB |
| Framework on main thread | None (vanilla JS) | Blazor runtime | React runtime |

---

## 🛡️ Security & Verification

Every release of Cepha CLI is built through a **secure GitHub Actions pipeline** with multi-layer verification:

| Check | Description |
|-------|-------------|
| 🔏 **Build Provenance** (SLSA) | Cryptographic proof that this package was built from source |
| 📋 **SBOM** | Software Bill of Materials (SPDX) — full dependency tree |
| 🛡️ **Vulnerability Scan** | All dependencies checked against known CVE databases |
| ✅ **Smoke Test** | Automated install + run verification on every release |

### Verify Package Integrity

You can independently verify that any Cepha CLI release was built from this repository:

```bash
# 1. Install the tool
dotnet tool install --global Cepha.CLI

# 2. Download the .nupkg for verification (replace VERSION with actual version)
curl -L -o cepha-cli.nupkg https://www.nuget.org/api/v2/package/Cepha.CLI/VERSION

# 3. Verify provenance (requires GitHub CLI: https://cli.github.com)
gh attestation verify cepha-cli.nupkg --owner sbay-dev
```

Alternatively, find the locally cached package after install:

```
Windows:  %USERPROFILE%\.dotnet\tools\.store\cepha.cli\VERSION\cepha.cli\VERSION\cepha.cli.VERSION.nupkg
Linux:    ~/.dotnet/tools/.store/cepha.cli/VERSION/cepha.cli/VERSION/cepha.cli.VERSION.nupkg
```

### View Attestations on GitHub

All attestations and security reports (SBOM, vulnerability scan) are attached to each [GitHub Release](https://github.com/sbay-dev/WasmMvcRuntime/releases).

---

## License

MIT © [sbay-dev](https://github.com/sbay-dev)

---

*Built with 🧬 **NetWasmMvc.SDK** — ASP.NET MVC, sovereign in the browser.*
