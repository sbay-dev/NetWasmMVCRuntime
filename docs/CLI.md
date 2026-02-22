# CLI Reference

**Cepha.CLI** is the command-line tool for creating, developing, and deploying WasmMvcRuntime applications.

[![NuGet](https://img.shields.io/nuget/v/Cepha.CLI?logo=nuget)](https://www.nuget.org/packages/Cepha.CLI)

---

## Installation

```bash
dotnet tool install --global Cepha.CLI
```

Update to the latest version:
```bash
dotnet tool update --global Cepha.CLI
```

---

## Commands

### `cepha new <name>` — Create a New Project

Scaffolds a complete MVC WASM application with the latest SDK version.

```bash
cepha new MyApp                  # Standard project
cepha new MyApp --identity       # With authentication system
cepha new MyApp --benchmark      # With performance tests
```

**What it creates:**
- `.csproj` with `NetWasmMvc.SDK` reference
- `Program.cs` with `CephaApp.Create()` bootstrap
- `Controllers/HomeController.cs` with sample actions
- `Views/Home/Index.cshtml` and `Views/Shared/_Layout.cshtml`
- `wwwroot/` with static assets

**SDK Version Resolution:**
The CLI dynamically resolves the latest `NetWasmMvc.SDK` version from NuGet at runtime. If offline, it falls back to a built-in default version.

---

### `cepha dev` — Development Server

Starts a development server with live reload.

```bash
cepha dev
```

---

### `cepha kit` — Node.js API Server

Starts the CephaKit Node.js server, running the same MVC controllers as an HTTP API.

```bash
cepha kit                        # Standard Node.js server
cepha kit --wrangler             # Via Cloudflare Wrangler
```

---

### `cepha publish` — Build for Production

Builds the application as static files ready for deployment.

```bash
cepha publish                    # Build static files
cepha publish local              # Local output
cepha publish cf                 # Deploy to Cloudflare Pages
cepha publish azure              # Deploy to Azure Static Web Apps
```

---

### `cepha info` — Project Information

Displays SDK version, project routes, and configuration details.

```bash
cepha info
```

---

### `cepha update` — Check for Updates

Checks for newer versions of the SDK and CLI.

```bash
cepha update
```

---

### `cepha benchmark` — Performance Tests

Runs UI stress tests and performance benchmarks.

```bash
cepha benchmark
```

---

## Interactive Mode

Running `cepha` without arguments enters the **interactive menu**:

```
  🧬 Cepha CLI v1.0.48
  ─────────────────────

  Main Menu — Select a command:

  ❯ 🆕  New Project       — Create a new MVC WASM app
    🚀  Dev Server        — Start development server
    🔌  API Server        — Start Node.js API server
    📦  Publish           — Build & deploy for production
    ℹ️   Info              — Show project info
    📈  Benchmark         — Run performance tests
    🔄  Update            — Check for updates
    ❓  Help              — Show all commands
    🚪  Exit              — Quit CLI
```

### Navigation

- **Arrow keys** — Move selection
- **Enter** — Select option
- **Escape** — Return to parent menu
- Sub-menus have a **🔙 Back to Main Menu** option
- The menu persists until you explicitly choose **Exit** or press Escape from the main menu

### Sub-Menus

**New Project:**
```
  🆕 New Project
  ─────────────

  ❯ 📄  Standard          — Basic MVC WASM app
    🔐  With Identity     — Include authentication
    📊  With Benchmark    — Include performance tests
    🔙  Back to Main Menu
```

**Publish:**
```
  📦 Publish
  ─────────

  ❯ 💻  Local Build       — Build static files
    ☁️   Cloudflare Pages  — Deploy to CF Pages
    🔷  Azure Static Apps — Deploy to Azure
    🔙  Back to Main Menu
```

---

## Configuration

The CLI reads configuration from the project's `.csproj` file. The SDK version is specified in the project SDK attribute:

```xml
<Project Sdk="NetWasmMvc.SDK/1.0.6">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

---

## Source

| File | Purpose |
|------|---------|
| [`Cepha.CLI/`](../Cepha.CLI/) | CLI tool source |
| [`Commands/NewCommand.cs`](../Cepha.CLI/Commands/NewCommand.cs) | Project scaffolding + dynamic SDK version |
| [`Commands/InteractiveMenu.cs`](../Cepha.CLI/Commands/InteractiveMenu.cs) | Persistent interactive menu |
| [NuGet Package](https://www.nuget.org/packages/Cepha.CLI) | Published CLI tool |
