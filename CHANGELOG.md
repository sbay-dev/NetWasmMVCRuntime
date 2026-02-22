# Changelog

All notable changes to this project will be documented in this file.

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
