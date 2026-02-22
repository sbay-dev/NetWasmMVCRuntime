# Architecture

## Overview

WasmMvcRuntime runs a complete ASP.NET MVC application inside a **WebAssembly Web Worker** in the browser. The main thread acts as a thin display surface — it never executes .NET code, controller logic, or database queries.

---

## Thread Model

| Thread | Role | Blocks UI? |
|--------|------|------------|
| **Main Thread** | DOM rendering, SPA routing, event listeners, CSS/script activation | Never |
| **Runtime Worker** | .NET WASM execution: MVC pipeline, EF Core, Identity, SignalR | N/A (off-thread) |
| **Data Worker** | OPFS file I/O: database snapshots, session persistence, offline queue | N/A (off-thread) |

---

## Runtime Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Browser                                                         │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Main Thread (Display Surface)                             │  │
│  │  index.html → main.js → SPA Router                         │  │
│  │  • Link interception (history.pushState)                   │  │
│  │  • Form submission handler                                 │  │
│  │  • Frame buffer + requestAnimationFrame rendering          │  │
│  │  • CSS promotion + onload wait → document-order scripts    │  │
│  │  • SignalR client proxy                                    │  │
│  │  • Cross-tab auth sync (BroadcastChannel)                  │  │
│  └──────────────┬─────────────────────────────┬───────────────┘  │
│                 │ postMessage()                │ postMessage()    │
│  ┌──────────────▼───────────────┐  ┌──────────▼──────────────┐  │
│  │  Runtime Worker              │  │  Data Worker             │  │
│  │  (.NET WASM via dotnet.js)   │  │  (OPFS I/O)             │  │
│  │                              │  │                          │  │
│  │  ┌────────────────────────┐  │  │  • Database snapshots    │  │
│  │  │  .NET Runtime          │  │  │  • Session persistence   │  │
│  │  │                        │  │  │  • Offline change queue  │  │
│  │  │  MvcEngine             │  │  │  • File operations       │  │
│  │  │  ├─ Controller Scan    │  │  │                          │  │
│  │  │  ├─ Route Matching     │  │  └──────────────────────────┘  │
│  │  │  ├─ Action Invocation  │  │                                │
│  │  │  └─ View Rendering     │  │        ┌──────────────────┐   │
│  │  │                        │  │        │  Origin Private   │   │
│  │  │  RazorTemplateEngine   │  │        │  File System      │   │
│  │  │  EF Core + SQLite      │  │        │  (Persistent)     │   │
│  │  │  Identity + Sessions   │  │        │                   │   │
│  │  │  SignalR (in-process)  │  │        │  • identity.db    │   │
│  │  └────────────────────────┘  │        │  • app-data.db    │   │
│  └──────────────────────────────┘        │  • sessions.json  │   │
│                                           └──────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Bootstrap Sequence

```
1. Browser loads index.html (static file from CDN)
2. index.html imports main.js (ES module)
3. main.js spawns Web Worker (cepha-runtime-worker.js)
4. Worker boots .NET WASM runtime (dotnet.js)
5. Worker registers JSImport/JSExport bindings
6. Worker calls Program.Main() → CephaApp.Create() → app.RunAsync()
7. RunAsync:
   a. Restores SQLite database from OPFS
   b. Restores session state from OPFS
   c. Ensures EF Core tables exist (auto-migration)
   d. Navigates to initial URL path
   e. Enters infinite event loop (Task.Delay(Timeout.Infinite))
8. Main thread intercepts link clicks → postMessage to Worker
9. Worker: MvcEngine.ProcessRequestAsync() → Controller → View → HTML
10. Worker: postMessage HTML frame to Main thread
11. Main thread: applies frame via requestAnimationFrame
```

---

## Rendering Pipeline

### Frame-Based Rendering

The MVC engine renders complete HTML for each navigation. The main thread receives this as a "frame" and applies it to the DOM:

1. **Controller executes** → produces `ViewResult` with model data
2. **RazorTemplateEngine** renders `.cshtml` template with model/ViewData
3. **Layout applied** → `@RenderBody()` + `@RenderSection()` merged
4. **HTML frame** sent to main thread via `postMessage`
5. **`activateScripts()`** processes the frame:
   - CSS `<link>` elements promoted and awaited (prevents FOUC)
   - `<script>` elements cloned and executed in document order
   - Each script's `onload` is awaited before the next executes
6. **Frame applied** via `requestAnimationFrame` (consistent 60fps)

### SPA Navigation

Links are intercepted at the main thread level:
- `click` events on `<a>` elements → `history.pushState()` + Worker message
- `<form>` submissions → serialize form data + Worker message
- `popstate` events (browser back/forward) → Worker message
- Worker processes the MVC request → returns new HTML frame

---

## Communication Protocol

```
Main Thread ←→ Runtime Worker:
  navigate(path)                → HTML frame
  submit(action, formData)      → HTML frame or redirect instruction
  hub-connect(hubName)          → connectionId
  hub-invoke(hub, method, args) → result JSON
  hub-event(hub, method, args)  → broadcast to listeners
  auth-sync()                   → re-render current page

Runtime Worker ←→ Data Worker (via Main Thread relay):
  cephaDb.persist(base64)       → ack
  cephaDb.restore()             → base64 database snapshot
  opfs.write(path, data)        → ack
  opfs.read(path)               → data
```

---

## JS Interop Model

All C#↔JavaScript communication uses the `System.Runtime.InteropServices.JavaScript` APIs:

**JSExport** — C# methods callable from JavaScript:
```csharp
[JSExport]
public static async Task<string> HandleNavigate(string path)
{
    return await MvcEngine.ProcessRequestAsync(path);
}
```

**JSImport** — JavaScript functions callable from C#:
```csharp
[JSImport("globalThis.cephaInterop.updateFrame")]
public static partial void UpdateFrame(string html);
```

This avoids dependency on Blazor's `IJSRuntime` and the Blazor component infrastructure.

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Web Worker hosting | .NET execution never blocks UI. Even complex DB queries or slow controllers cannot cause jank. |
| Embedded `.cshtml` resources | Avoids shipping the Razor compilation toolchain to the browser. Pattern-matching covers the most common Razor features. |
| OPFS for persistence | Unlike `localStorage` (5MB, synchronous, main-thread only), OPFS supports large files, async access, and Worker-thread access. |
| Frame-based rendering | Simpler than virtual DOM diffing. Each navigation produces a complete HTML document — no incremental patching needed. |
| JSImport/JSExport | Direct interop without Blazor's marshaling layer. Lower overhead, explicit contract. |

---

## Source Files

| File | Purpose |
|------|---------|
| [`MvcEngine.cs`](../WasmMvcRuntime.Core/MvcEngine.cs) | Controller discovery, route matching, action invocation |
| [`RazorTemplateEngine.cs`](../WasmMvcRuntime.Abstractions/Views/RazorTemplateEngine.cs) | `.cshtml` rendering |
| [`CephaApp.cs`](../NetWasmMvc.SDK/shared/CephaApp.cs) | Application bootstrap, DB restore, event loop |
| [`JsInterop.cs`](../NetWasmMvc.SDK/shared/JsInterop.cs) | JSImport declarations |
| [`JsExports.cs`](../NetWasmMvc.SDK/shared/JsExports.cs) | JSExport entry points |
| [`main.js`](../NetWasmMvc.SDK/content/wwwroot/main.js) | SPA router, frame pipeline, activateScripts |
| [`cepha-runtime-worker.js`](../NetWasmMvc.SDK/content/wwwroot/cepha-runtime-worker.js) | .NET WASM host in Web Worker |
