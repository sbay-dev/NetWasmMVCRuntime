# [Proposal] Client-Side ASP.NET MVC Runtime for WebAssembly

## Summary

This proposal introduces a **client-side ASP.NET MVC runtime** that executes Controllers, Razor Views, Routing, EF Core (SQLite), SignalR, and Identity entirely inside a WebAssembly Web Worker — with zero server dependency at runtime.

Developers write standard MVC code — Controllers, `.cshtml` Views, `ViewData`/`ViewBag`/`TempData`, Tag Helpers, Areas — and the SDK compiles it into **static files** deployable to any CDN, edge network, or embedded WebView.

> **A working implementation exists today:**
> [`NetWasmMvc.SDK`](https://www.nuget.org/packages/NetWasmMvc.SDK) + [`Cepha.CLI`](https://www.nuget.org/packages/Cepha.CLI) on NuGet — source at [sbay-dev/NetWasmMVCRuntime](https://github.com/sbay-dev/NetWasmMVCRuntime)

---

## The Problem

ASP.NET MVC is the most widely adopted .NET web programming model — yet it has **no client-side story**.

- **MVC requires a server.** Developers with large MVC codebases have no path to deploy as static SPAs or to edge runtimes without a full rewrite.
- **No offline-first MVC.** Server-rendered MVC cannot function without connectivity. There is no PWA-capable MVC with local Controllers, Views, and database access.
- **No "write once, deploy anywhere."** You must choose between server-rendered MVC (requires infrastructure) or Blazor (different programming model). No option to run the same code as a browser SPA, a Node.js API, or an edge Worker.
- **Competitive gap.** Next.js, Nuxt, and SvelteKit all support static-site generation and edge deployment. .NET MVC has no equivalent.

---

## Proposed Solution

A new SDK target that compiles ASP.NET MVC for execution inside a WebAssembly Web Worker:

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly.Mvc">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
</Project>
```

The developer writes **standard MVC code** — no new paradigm to learn:

```csharp
public class HomeController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Welcome";
        return View();
    }

    [HttpPost]
    public IActionResult Contact(ContactForm form)
    {
        if (!ModelState.IsValid) return View(form);
        _db.Messages.Add(new Message { Name = form.Name, Body = form.Body });
        _db.SaveChanges();
        TempData["Success"] = "Message saved locally.";
        return RedirectToAction("Index");
    }
}
```

`dotnet publish` produces static files. No ASP.NET server required at runtime.

**How it works:** The .NET runtime boots inside a Web Worker, keeping the UI thread free. An MVC engine handles controller discovery, route matching, and action invocation. A pattern-matching Razor engine renders `.cshtml` views from embedded resources. The rendered HTML is sent to the main thread as frames applied via `requestAnimationFrame`. SQLite databases and session state persist to the Origin Private File System (OPFS). All C#↔JS communication uses JSImport/JSExport — no dependency on Blazor's infrastructure.

### What's included

- **Full MVC pipeline** — `Controller` base class, `ViewData`/`ViewBag`/`TempData`/`ModelState`, `ViewResult`/`PartialViewResult`/`JsonResult`/`RedirectResult`, attribute routing, `[Area]`, `[ApiController]`, convention-based routing.
- **Razor rendering (no Roslyn)** — `@Model`, `@ViewData`, `@if`/`@foreach`, `@RenderBody()`, `@RenderSection()`, `@Html.PartialAsync()`, `asp-controller`/`asp-action` tag helpers.
- **EF Core + SQLite in WASM** — Full LINQ, migrations, relationships. Database persisted to OPFS.
- **Client-side Identity** — PBKDF2-SHA256 hashing, HMAC-signed session tokens, fingerprint binding, account lockout, cross-tab auth sync.
- **In-process SignalR** — Hub discovery, `Clients.All`/`Caller`/`Others`/`Group()`, connection management — all in-process.
- **Dual hosting** — Same code runs as a browser SPA **or** a Node.js/Cloudflare Workers/Edge API server.
- **CLI tooling** — `cepha new`, `cepha dev`, `cepha publish`, `cepha kit` ([`Cepha.CLI`](https://www.nuget.org/packages/Cepha.CLI)).

### How this complements Blazor

This is **not** a replacement for Blazor. **Blazor excels** at rich interactive UIs with fine-grained reactivity. **MVC WASM excels** at document-oriented apps, admin panels, and CRUD applications where the existing MVC skillset should transfer directly to client-side deployment — without rewriting views as components. Both models share the same .NET WASM runtime and could coexist.

---

## Why This Belongs in .NET 11

1. **The runtime foundation is ready.** .NET 10 ships mature WebAssembly support: `dotnet.js`, Web Workers, JSImport/JSExport, WASM-compiled SQLite. What's missing is the MVC engine layer on top.

2. **NativeAOT for WASM is coming.** .NET 11 is expected to advance NativeAOT/Trimming for WebAssembly, which would reduce the current ~15-25MB bundle to a competitive size. This proposal aligns with that timeline.

3. **Edge computing is mainstream.** Cloudflare Workers, Deno Deploy, Vercel Edge Functions are standard deployment targets. .NET 11 is the window to establish MVC as a viable edge framework.

4. **Developer demand is real.** MVC is the #1 ASP.NET programming model by adoption. Developers want their existing skills to work in new deployment targets — not a new paradigm.

5. **Feasibility is proven.** The [reference implementation](https://github.com/sbay-dev/NetWasmMVCRuntime) is published on NuGet with CI/CD, covering Controllers → Views → EF Core → Identity → SignalR. This is not theoretical.

---

## What We Are Asking from the .NET Team

We are not asking for a massive new framework — just **targeted infrastructure**:

### Experimental / .NET 11 Preview

1. **`Microsoft.NET.Sdk.WebAssembly.Mvc` SDK target** — embed `.cshtml` as resources, link SQLite natively, produce static SPA output. ([~200 lines of MSBuild](https://github.com/sbay-dev/WasmMvcRuntime/tree/sdk-release-1.0.1/NetWasmMvc.SDK/Sdk) in our implementation.)
2. **Lightweight `MvcEngine` for WASM** — controller discovery, route matching, action invocation without the full server pipeline. ([Single file](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/WasmMvcRuntime.Core/MvcEngine.cs) in our implementation.)
3. **Razor template provider contract** (`ITemplateProvider` / `IRazorTemplateEngine`) — rendering `.cshtml` from embedded resources.
4. **`dotnet new mvc-wasm` template** — scaffolding via the standard template system.

### Post-Preview

5. NativeAOT/Trimming for smaller bundles.
6. Visual Studio / VS Code F5 debugging in WASM Workers.
7. Blazor interop — embed MVC views inside Blazor components and vice versa.

**What we are NOT asking for:** ❌ Changes to the existing MVC server pipeline · ❌ Changes to Blazor · ❌ A new component model · ❌ Deprecation of any existing feature.

---

## Reference Implementation

📦 **Repository:** [github.com/sbay-dev/NetWasmMVCRuntime](https://github.com/sbay-dev/NetWasmMVCRuntime)

**NuGet packages:**
- SDK: [`NetWasmMvc.SDK`](https://www.nuget.org/packages/NetWasmMvc.SDK) — `<Project Sdk="NetWasmMvc.SDK/1.0.6">`
- CLI: [`Cepha.CLI`](https://www.nuget.org/packages/Cepha.CLI) — `dotnet tool install --global Cepha.CLI`

Key source files: [`MvcEngine.cs`](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/WasmMvcRuntime.Core/MvcEngine.cs) · [`RazorTemplateEngine.cs`](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/WasmMvcRuntime.Abstractions/Views/RazorTemplateEngine.cs) · [`Controller.cs`](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/WasmMvcRuntime.Abstractions/Controller.cs) · [`SignalREngine.cs`](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/WasmMvcRuntime.Core/SignalREngine.cs) · [`Identity/`](https://github.com/sbay-dev/WasmMvcRuntime/tree/sdk-release-1.0.1/WasmMvcRuntime.Identity) · [`CephaApp.cs`](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/NetWasmMvc.SDK/shared/CephaApp.cs) · [`main.js`](https://github.com/sbay-dev/WasmMvcRuntime/blob/sdk-release-1.0.1/NetWasmMvc.SDK/content/wwwroot/main.js) · [`Sdk/`](https://github.com/sbay-dev/WasmMvcRuntime/tree/sdk-release-1.0.1/NetWasmMvc.SDK/Sdk)

---

## Call for Feedback

We'd love to hear from the community and the .NET team:

1. **Is this worth supporting officially?** Do enough developers want MVC in the browser to justify framework-level investment?
2. **Should this start as an experimental SDK?** Similar to how Blazor started — let the community validate the model first.
3. **What MVC features are essential for a v1?** The current implementation covers Controllers, Views, EF Core, Identity, and SignalR. What else would you need?
4. **Bundle size concerns?** The ~15-25MB size is the biggest barrier. Would NativeAOT improvements in .NET 11 make this viable for your use case?
5. **Edge deployment interest?** Is running .NET MVC on Cloudflare Workers / Deno Deploy compelling for your team?

If you find this idea interesting, please 👍 this discussion and share your use cases. Community signal helps the .NET team prioritize.

---

*Based on [WasmMvcRuntime](https://github.com/sbay-dev/NetWasmMVCRuntime) — an open-source client-side ASP.NET MVC runtime for WebAssembly, published on NuGet and actively maintained.*
