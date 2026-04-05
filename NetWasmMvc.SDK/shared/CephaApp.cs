using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.Core;
using WasmMvcRuntime.Identity.Security;
using WasmMvcRuntime.Identity.Services;

namespace Cepha;

/// <summary>
/// 🧬 CephaApp — one-call bootstrap for NetWasmMvc applications.
/// <code>
/// var app = CephaApp.Create();
/// await app.RunAsync();
/// </code>
/// </summary>
[SupportedOSPlatform("browser")]
public static class CephaApp
{
    /// <summary>
    /// Creates a Cepha application with MVC + SignalR engines pre-configured.
    /// </summary>
    /// <param name="configureServices">Register your own services (DbContext, Repositories, etc.)</param>
    public static CephaApplication Create(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();

        // MVC Engine — auto-discovers controllers from all loaded assemblies
        services.AddSingleton<IMvcEngine, MvcEngine>();

        // SignalR Engine — auto-discovers hubs from all loaded assemblies
        services.AddSingleton<ISignalREngine>(sp =>
        {
            var engine = new SignalREngine(sp);
            engine.OnClientEvent = (hubName, method, connectionId, argsJson) =>
                JsInterop.DispatchHubEvent(hubName, method, connectionId, argsJson);
            return engine;
        });

        // User's custom services
        configureServices?.Invoke(services);

        // Register ILogger<> open generic — browser console logger outputs to DevTools
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
                              typeof(Microsoft.Extensions.Logging.BrowserConsoleLogger<>));

        var provider = services.BuildServiceProvider();
        return new CephaApplication(provider);
    }
}

/// <summary>
/// A running Cepha application. Call <see cref="RunAsync"/> to start.
/// </summary>
[SupportedOSPlatform("browser")]
public class CephaApplication
{
    private readonly IServiceProvider _provider;
    private static IServiceProvider? _staticProvider;

    public CephaApplication(IServiceProvider provider)
    {
        _provider = provider;
        _staticProvider = provider;

        // Configure view engine (load embedded .cshtml templates)
        ViewResult.Configure();

        // 🧬 Wire Atomic Probes — centralized identity triggers
        // When login/logout fires in SignInManager, the probe broadcasts
        // the auth change to ALL tabs via the video stream pipeline.
        WireIdentityTriggers(provider);

        // Wire MVC navigation handler
        var mvcEngine = provider.GetRequiredService<IMvcEngine>();

        JsExports.RegisterNavigateHandler(async (path) =>
        {
            using var scope = _provider.CreateScope();
            var context = new InternalHttpContext
            {
                Path = path,
                Method = "GET",
                RequestServices = scope.ServiceProvider
            };
            await InjectSessionItems(context, scope.ServiceProvider);
            await mvcEngine.ProcessRequestAsync(context);

            // Persist sessions if state changed (login/logout via GET)
            await PersistSessionsIfDirtyAsync();

            // Handle SPA redirect (302 → client-side navigation)
            if (context.StatusCode == 302 && !string.IsNullOrEmpty(context.ResponseBody))
            {
                await JsInterop.NavigateTo(context.ResponseBody);
                return;
            }

            if (!string.IsNullOrEmpty(context.ResponseBody))
            {
                if (context.ContentType == "application/json")
                {
                    var escaped = context.ResponseBody
                        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                    JsInterop.SetInnerHTML("#app",
                        $"<pre style='background:#1e1e1e;color:#d4d4d4;padding:20px;border-radius:8px;overflow:auto;'>{escaped}</pre>");
                }
                else
                {
                    JsInterop.SetInnerHTML("#app", context.ResponseBody);
                }
            }
        });

        JsExports.RegisterFormSubmitHandler(async (action, formDataJson) =>
        {
            int statusCode;
            string? responseBody;
            string? contentType;

            using (var scope = _provider.CreateScope())
            {
                var context = new InternalHttpContext
                {
                    Path = action,
                    Method = "POST",
                    RequestServices = scope.ServiceProvider
                };

                // Parse form data JSON into context
                if (!string.IsNullOrEmpty(formDataJson))
                {
                    var formDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(formDataJson);
                    if (formDict != null)
                    {
                        foreach (var kvp in formDict)
                            context.FormData[kvp.Key] = kvp.Value;
                    }
                }

                await InjectSessionItems(context, scope.ServiceProvider);
                await mvcEngine.ProcessRequestAsync(context);

                statusCode = context.StatusCode;
                responseBody = context.ResponseBody;
                contentType = context.ContentType;
            }
            // Scope disposed — all EF Core connections closed, SQLite flushed

            // Auto-persist database + sessions after form submissions
            await PersistDatabaseAsync();
            await PersistSessionsIfDirtyAsync();

            // Handle SPA redirect
            if (statusCode == 302 && !string.IsNullOrEmpty(responseBody))
            {
                await JsInterop.NavigateTo(responseBody);
                return;
            }

            if (!string.IsNullOrEmpty(responseBody))
            {
                JsInterop.SetInnerHTML("#app", responseBody);
            }
        });

        JsExports.RegisterFetchRouteHandler(async (path) =>
        {
            using var scope = _provider.CreateScope();
            var context = new InternalHttpContext
            {
                Path = path,
                Method = "GET",
                RequestServices = scope.ServiceProvider
            };
            await mvcEngine.ProcessRequestAsync(context);
            return context.ResponseBody ?? "";
        });

        // Wire SignalR handlers — expression lambdas
        var signalR = provider.GetRequiredService<ISignalREngine>();
        JsExports.RegisterHubConnectHandler(hubName => signalR.ConnectAsync(hubName));
        JsExports.RegisterHubDisconnectHandler((hubName, connId) => signalR.DisconnectAsync(hubName, connId));
        JsExports.RegisterHubInvokeHandler((hubName, method, connId, argsJson) => signalR.InvokeAsync(hubName, method, connId, argsJson));

        // Wire CephaKit server-side request handler
        JsExports.RegisterHandleRequestHandler(async (method, path, headersJson, body) =>
        {
            using var scope = _provider.CreateScope();
            var context = new InternalHttpContext
            {
                Path = path,
                Method = method,
                RequestBody = body,
                RequestServices = scope.ServiceProvider
            };

            // Parse form data from body (for POST requests)
            if (method == "POST" && !string.IsNullOrEmpty(body))
            {
                try
                {
                    var formDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                    if (formDict != null)
                        foreach (var kvp in formDict)
                            context.FormData[kvp.Key] = kvp.Value;
                }
                catch { /* body is not form-encoded JSON — will be handled by [FromBody] */ }
            }

            await mvcEngine.ProcessRequestAsync(context);

            // Post-process HTML responses: resolve ASP.NET conventions
            var contentType = context.ContentType ?? "text/html";
            if (contentType.Contains("html") && !string.IsNullOrEmpty(context.ResponseBody))
            {
                context.ResponseBody = PostProcessHtml(context.ResponseBody);
            }

            var statusCode = context.StatusCode != 0 ? context.StatusCode
                : string.IsNullOrEmpty(context.ResponseBody) ? 404 : 200;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                statusCode,
                contentType,
                body = context.ResponseBody ?? ""
            });
        });
    }

    /// <summary>The DI service provider. Use for resolving services or creating scopes.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>
    /// Enables CephaKit server auto-discovery.
    /// Silently skips when running in Node.js (cepha kit) where the JS binding is unavailable.
    /// </summary>
    /// <param name="port">CephaKit port (default: 3001)</param>
    public CephaApplication EnableCephaKit(int port = 3001)
    {
        try
        {
            JsInterop.StartCephaKit(port);
        }
        catch (Exception)
        {
            // CephaKit JS binding not available (e.g., running inside cepha kit Node.js)
        }
        return this;
    }

    /// <summary>
    /// Starts the Cepha application — restores DB from OPFS, navigates to initial page.
    /// </summary>
    /// <param name="defaultPath">Default route if URL path is empty (default: "/" — resolved by MvcEngine)</param>
    public async Task RunAsync(string defaultPath = "/")
    {
        JsInterop.DevLog("🧬 Cepha is starting...");

        // Set client fingerprint for session binding
        try { SessionStorageService.SetClientFingerprint(JsInterop.GetFingerprint()); }
        catch { /* Worker may not have fingerprint */ }

        // Never let startup storage stages block first render indefinitely.
        await RunStartupStageAsync("restore-db", () => RestoreDatabaseAsync(), TimeSpan.FromSeconds(2));
        await RunStartupStageAsync("restore-sessions", () => RestoreSessionsFromOpfsAsync(), TimeSpan.FromSeconds(2));
        await RunStartupStageAsync("ensure-db", () => EnsureDatabaseAsync(), TimeSpan.FromSeconds(3));

        var currentPath = JsInterop.GetCurrentPath() is { Length: > 0 } p ? p : defaultPath;

        var rendered = await RunStartupStageAsync(
            $"navigate:{currentPath}",
            () => JsExports.Navigate(currentPath),
            TimeSpan.FromSeconds(8));

        if (!rendered)
        {
            JsInterop.SetInnerHTML(
                "#app",
                "<div style='padding:24px;font-family:system-ui'>" +
                "<h3 style='margin:0 0 8px;color:#b91c1c'>Startup timeout</h3>" +
                "<p style='margin:0;color:#374151'>Cepha runtime started but initial route rendering did not complete.</p>" +
                "</div>");
        }

        var mvcEngine = _provider.GetRequiredService<IMvcEngine>() as MvcEngine;
        var signalR = _provider.GetRequiredService<ISignalREngine>();
        var routes = mvcEngine?.GetRoutes()?.Count ?? 0;
        var hubs = signalR.GetHubNames();

        JsInterop.ConsoleLog($"✅ Cepha ready — {routes} routes, {hubs.Count} hubs ({string.Join(", ", hubs)})");

        // Block forever — WASM event loop (TaskCompletionSource avoids Monitor overhead)
        await new TaskCompletionSource().Task;
    }

    private static async Task<bool> RunStartupStageAsync(string stage, Func<Task> action, TimeSpan timeout)
    {
        try
        {
            await action().WaitAsync(timeout);
            JsInterop.DevLog($"🧬 Startup stage completed: {stage}");
            return true;
        }
        catch (TimeoutException)
        {
            JsInterop.ConsoleWarn($"🧬 Startup stage timed out: {stage}");
            return false;
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleWarn($"🧬 Startup stage failed ({stage}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores the SQLite database from OPFS Worker storage.
    /// Called automatically on startup before EF Core initializes.
    /// </summary>
    public static async Task RestoreDatabaseAsync(string dbFileName = "identity.db")
    {
        try
        {
            var base64 = await JsInterop.RestoreDbFromOPFS();
            if (!string.IsNullOrEmpty(base64))
            {
                var bytes = Convert.FromBase64String(base64);
                File.WriteAllBytes(dbFileName, bytes);
                JsInterop.DevLog($"🗄️ Database restored from OPFS ({bytes.Length:N0} bytes)");
            }
            else
            {
                JsInterop.DevLog("🗄️ No database snapshot in OPFS — fresh start");
            }
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleWarn($"🗄️ Database restore skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Persists the SQLite database to OPFS Worker storage.
    /// Flushes SQLite WAL/journal first to ensure all data is in the main file.
    /// </summary>
    public static async Task PersistDatabaseAsync(string dbFileName = "identity.db")
    {
        try
        {
            // Flush SQLite WAL to main DB file using a fresh DbContext
            try
            {
                if (_staticProvider != null)
                {
                    using var scope = _staticProvider.CreateScope();
                    var db = scope.ServiceProvider.GetService<DbContext>();
                    if (db != null)
                    {
                        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
                        var conn = db.Database.GetDbConnection();
                        if (conn.State == System.Data.ConnectionState.Open)
                            await conn.CloseAsync();
                    }
                }
            }
            catch { /* WAL checkpoint optional */ }

            if (File.Exists(dbFileName))
            {
                var bytes = File.ReadAllBytes(dbFileName);
                var base64 = Convert.ToBase64String(bytes);
                await JsInterop.PersistDbToOPFS(base64);
                JsInterop.DevLog($"🗄️ Database persisted to OPFS ({bytes.Length:N0} bytes)");
            }
            else
            {
                JsInterop.ConsoleWarn("🗄️ Database file not found for persist");
            }
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleWarn($"🗄️ Database persist failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore session state (HMAC key + active sessions) from OPFS.
    /// Called on startup and by SyncAuth (cross-tab broadcast).
    /// </summary>
    public static async Task RestoreSessionsFromOpfsAsync()
    {
        try
        {
            var json = await JsInterop.OpfsRead("cepha_sessions.json");
            if (SessionStorageService.RestoreState(json))
            {
                JsInterop.DevLog("🔐 Sessions restored from OPFS");
            }
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleWarn($"🔐 Session restore skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Persist session state to OPFS if any changes occurred (login/logout).
    /// Lightweight — only writes when dirty flag is set.
    /// </summary>
    private static async Task PersistSessionsIfDirtyAsync()
    {
        if (!SessionStorageService.IsDirty) return;
        try
        {
            var json = SessionStorageService.SerializeState();
            await JsInterop.OpfsWrite("cepha_sessions.json", json);
            JsInterop.DevLog($"🔐 Sessions persisted to OPFS ({json.Length} chars)");

            // Broadcast auth change to other tabs via BroadcastChannel
            try { JsInterop.BroadcastAuthChange("sync"); }
            catch { /* Broadcast optional */ }
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleWarn($"🔐 Session persist failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures EF Core database tables are created.
    /// Checks if a DbContext is registered and calls EnsureCreated.
    /// </summary>
    private async Task EnsureDatabaseAsync()
    {
        try
        {
            var dbContext = _provider.GetService<DbContext>();
            if (dbContext != null)
            {
                await dbContext.Database.EnsureCreatedAsync();
                JsInterop.DevLog("🗄️ Database tables ensured");
            }
        }
        catch (Exception ex)
        {
            JsInterop.ConsoleWarn($"🗄️ Database init skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Injects session/user info + IdentityAtom into context.Items so it's available as ViewBag in all views.
    /// Identity components can only render their authenticated state through the atom's RenderToken.
    /// </summary>
    private static async Task InjectSessionItems(InternalHttpContext context, IServiceProvider provider)
    {
        try
        {
            var sessionService = provider.GetService<WasmMvcRuntime.Identity.Services.ISessionStorageService>();
            if (sessionService == null) return;

            var session = await sessionService.GetSessionAsync();
            if (session != null)
            {
                context.Items["UserName"] = session.UserName;
                context.Items["UserEmail"] = session.Email;
                context.Items["UserFullName"] = session.FullName;
                context.Items["IsAuthenticated"] = "true";

                // 🔬 IdentityAtom — reforge for current page, inject RenderToken
                var atom = SessionStorageService.ActiveAtom;
                if (atom != null && atom.IsCoherent)
                {
                    SessionStorageService.ReforgeAtomForPage(context.Path);
                    context.Items["AtomRenderToken"] = atom.RenderTokenHex();
                    context.Items["AtomCoherent"] = "true";
                    context.Items["AtomGrants"] = ((uint)atom.GetGrants()).ToString();

                    // Grant-level flags for identity components (bitfield checks)
                    var grants = atom.GetGrants();
                    if ((grants & IdentityAtom.RenderGrant.LoginPartial) != 0)
                        context.Items["AtomGrantLoginPartial"] = "true";
                    if ((grants & IdentityAtom.RenderGrant.AdminPanel) != 0)
                        context.Items["AtomGrantAdmin"] = "true";
                    if ((grants & IdentityAtom.RenderGrant.RoleIndicator) != 0)
                        context.Items["AtomGrantRoles"] = "true";
                }
            }
            else
            {
                context.Items["IsGuest"] = "true";
                context.Items["AtomCoherent"] = "false";
            }
        }
        catch { /* Identity not registered — skip */ }
    }

    /// <summary>
    /// 🧬 Atomic Probes — wires IdentityTriggers so login/logout
    /// automatically broadcasts auth state change through the video stream
    /// to ALL tabs. The View.cshtml is the plasma source — this is the detonator.
    /// </summary>
    private static void WireIdentityTriggers(IServiceProvider provider)
    {
        try
        {
            var triggers = provider.GetService<IdentityTriggers>();
            if (triggers == null) return;

            triggers.OnSignedIn = async (userId, userName, roles) =>
            {
                JsInterop.DevLog($"🧬 AtomicProbe: {userName} signed in [{string.Join(",", roles)}]");
                // Persist + broadcast immediately — all tabs get the new frame
                await PersistSessionsIfDirtyAsync();
            };

            triggers.OnSignedOut = async (userId, userName) =>
            {
                JsInterop.DevLog($"🧬 AtomicProbe: {userName} signed out");
                // Persist + broadcast immediately — all tabs re-render as guest
                await PersistSessionsIfDirtyAsync();
            };

            triggers.OnSignInFailed = (userName, reason) =>
            {
                JsInterop.ConsoleWarn($"🧬 AtomicProbe: {userName} sign-in failed — {reason}");
            };

            triggers.OnLockedOut = (userName, until) =>
            {
                JsInterop.ConsoleWarn($"🧬 AtomicProbe: {userName} locked out until {until:HH:mm}");
            };

            JsInterop.DevLog("🧬 Atomic Probes wired to IdentityTriggers");
        }
        catch { /* Identity not registered — no probes */ }
    }

    /// <summary>
    /// Post-processes rendered HTML to resolve ASP.NET Core conventions that don't
    /// exist in browser-wasm: tilde-slash (~/) paths and asp-* tag helper attributes.
    /// </summary>
    private static string PostProcessHtml(string html)
    {
        // 1. Resolve ~/  →  /  in href, src, action attributes
        html = Regex.Replace(html, @"(href|src|action)\s*=\s*""~/", @"$1=""/");
        html = Regex.Replace(html, @"(href|src|action)\s*=\s*'~/", @"$1='/");

        // 2. Emulate asp-controller / asp-action tag helpers on <a> elements
        html = Regex.Replace(html, @"<a\b([^>]*?)>", m =>
        {
            var attrs = m.Groups[1].Value;
            if (!attrs.Contains("asp-controller")) return m.Value;

            var ctrl = Regex.Match(attrs, @"asp-controller\s*=\s*""([^""]*)""").Groups[1].Value;
            var action = Regex.Match(attrs, @"asp-action\s*=\s*""([^""]*)""").Groups[1].Value;
            var area = Regex.Match(attrs, @"asp-area\s*=\s*""([^""]*)""").Groups[1].Value;

            if (string.IsNullOrEmpty(ctrl)) return m.Value;

            var href = string.IsNullOrEmpty(area)
                ? $"/{ctrl}" + (string.IsNullOrEmpty(action) || action == "Index" ? "" : $"/{action}")
                : $"/{area}/{ctrl}" + (string.IsNullOrEmpty(action) || action == "Index" ? "" : $"/{action}");

            // Strip asp-* attributes, add href
            attrs = Regex.Replace(attrs, @"\s*asp-\w+\s*=\s*""[^""]*""", "");
            return $@"<a href=""{href}""{attrs}>";
        });

        // 3. Strip asp-append-version attributes (no-op in WASM)
        html = Regex.Replace(html, @"\s*asp-append-version\s*=\s*""[^""]*""", "");

        // 4. Strip empty <script type="importmap"></script> (causes console noise)
        html = Regex.Replace(html, @"<script\s+type=""importmap""\s*>\s*</script>", "");

        return html;
    }
}
