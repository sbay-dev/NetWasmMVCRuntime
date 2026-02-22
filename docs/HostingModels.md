# Hosting Models

WasmMvcRuntime supports multiple hosting models from the same compiled codebase. The MVC controllers, views, and business logic remain identical — only the hosting environment changes.

---

## Browser SPA (Default)

The primary hosting model. The application runs as a static Single-Page Application in the browser.

### How it works

1. `dotnet publish` produces static files: `index.html`, `main.js`, `.wasm`, DLLs
2. Deploy to any static file host (CDN, GitHub Pages, Cloudflare Pages, Azure Static Web Apps, S3, nginx)
3. Browser loads `index.html` → boots .NET WASM in a Web Worker
4. All MVC processing happens client-side — no server needed

### Build & Deploy

```bash
# Build
dotnet publish -c Release

# Output is in bin/Release/net10.0/publish/wwwroot/
# Deploy this folder to any static host
```

### Characteristics

- ✅ Fully offline-capable
- ✅ Zero server infrastructure
- ✅ Database persisted to OPFS
- ✅ Identity system runs locally
- ⚠️ Bundle size: ~15-25MB (first load)
- ⚠️ Cold start: ~2-4 seconds

---

## Node.js API Server (CephaKit)

The same compiled WASM application runs inside Node.js, serving MVC responses over HTTP.

### How it works

1. `cepha-server.mjs` boots the .NET WASM runtime in Node.js
2. HTTP requests are routed to `MvcEngine.ProcessRequestAsync()`
3. Controllers execute and return HTML/JSON responses
4. The Node.js process serves these responses over HTTP

### Usage

```bash
# Via CLI
cepha kit

# Or directly
node cepha-server.mjs
```

### Request Flow

```javascript
// cepha-server.mjs (simplified)
import { createServer } from 'http';

const handler = async (req, res) => {
    const body = await readBody(req);
    const response = await exports.HandleRequest(
        req.method, req.url, JSON.stringify(req.headers), body
    );
    res.writeHead(response.statusCode, { 'Content-Type': response.contentType });
    res.end(response.body);
};

createServer(handler).listen(3000);
```

### Characteristics

- ✅ Server-rendered MVC responses
- ✅ Same controllers and views as browser SPA
- ✅ Can serve API endpoints (`[ApiController]`)
- ✅ Runs on any Node.js 18+ host
- ⚠️ No OPFS (uses filesystem for SQLite)

---

## Edge Workers (Cloudflare Workers / Deno Deploy)

Deploy the same MVC application to edge computing platforms.

### Cloudflare Workers

```bash
# Via CLI
cepha kit --wrangler

# Or configure wrangler.toml manually
```

```toml
# wrangler.toml
name = "my-mvc-app"
main = "cepha-server.mjs"
compatibility_date = "2024-01-01"
compatibility_flags = ["nodejs_compat"]
```

### Deno Deploy

```typescript
import { serve } from "https://deno.land/std/http/server.ts";
// Boot .NET WASM and handle requests the same way
```

### Characteristics

- ✅ Global edge distribution
- ✅ Sub-100ms cold starts (V8 isolates)
- ✅ Same controllers and views
- ⚠️ No OPFS or persistent storage (use KV/D1 for data)

---

## Embedded WebView (MAUI / Electron / Tauri)

Ship the MVC application inside a native application shell.

### Concept

```
Native App Shell
└── WebView
    └── index.html → main.js → Web Worker → .NET WASM
        └── Full MVC pipeline + SQLite + Identity
```

### Use Cases

- Desktop applications with MVC UI
- Mobile applications (MAUI WebView)
- Kiosk / embedded systems
- Field applications (fully offline)

### Status: 🔲 Planned

This hosting model requires additional integration work for native file system access and WebView configuration.

---

## Comparison

| Feature | Browser SPA | Node.js API | Edge Workers | Embedded WebView |
|---------|-------------|-------------|--------------|------------------|
| Server required | ❌ | ✅ (Node.js) | ✅ (Edge platform) | ❌ |
| Offline capable | ✅ | ❌ | ❌ | ✅ |
| Database | SQLite + OPFS | SQLite + filesystem | Platform KV/D1 | SQLite + filesystem |
| Identity | Client-side | Server-side | Stateless | Client-side |
| Bundle delivery | Browser download | Server-local | Edge cache | App bundle |
| Cold start | ~2-4s | ~1-2s | <100ms | ~1-2s |
| Status | ✅ Complete | ✅ Complete | ✅ Complete | 🔲 Planned |

---

## Source Files

| File | Purpose |
|------|---------|
| [`CephaApp.cs`](../NetWasmMvc.SDK/shared/CephaApp.cs) | Browser SPA bootstrap |
| [`cepha-server.mjs`](../NetWasmMvc.SDK/content/wwwroot/cepha-server.mjs) | Node.js / Edge Worker host |
| [`cepha-runtime-worker.js`](../NetWasmMvc.SDK/content/wwwroot/cepha-runtime-worker.js) | Web Worker WASM host |
| [`main.js`](../NetWasmMvc.SDK/content/wwwroot/main.js) | Browser SPA engine |
