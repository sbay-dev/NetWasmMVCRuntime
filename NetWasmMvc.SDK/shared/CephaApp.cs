using System.Runtime.Versioning;
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

        // Wire SignalR handlers
        var signalR = provider.GetRequiredService<ISignalREngine>();
        JsExports.RegisterHubConnectHandler(async (hubName) =>
            await signalR.ConnectAsync(hubName));
        JsExports.RegisterHubDisconnectHandler(async (hubName, connId) =>
            await signalR.DisconnectAsync(hubName, connId));
        JsExports.RegisterHubInvokeHandler(async (hubName, method, connId, argsJson) =>
            await signalR.InvokeAsync(hubName, method, connId, argsJson));

        // Wire CephaKit server-side request handler
        JsExports.RegisterHandleRequestHandler(async (method, path, headersJson, body) =>
        {
            using var scope = _provider.CreateScope();
            var context = new InternalHttpContext
            {
                Path = path,
                Method = method,
                RequestServices = scope.ServiceProvider
            };

            // Parse form data from body (for POST requests via CephaKit)
            if (method == "POST" && !string.IsNullOrEmpty(body))
            {
                try
                {
                    var formDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                    if (formDict != null)
                        foreach (var kvp in formDict)
                            context.FormData[kvp.Key] = kvp.Value;
                }
                catch { }
            }

            await mvcEngine.ProcessRequestAsync(context);
            var contentType = context.ContentType ?? "text/html";
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
        JsInterop.ConsoleLog("🧬 Cepha is starting...");

        // Set client fingerprint for session binding
        try { SessionStorageService.SetClientFingerprint(JsInterop.GetFingerprint()); }
        catch { /* Worker may not have fingerprint */ }

        // Restore database from OPFS Worker (persistent storage)
        await RestoreDatabaseAsync();

        // Restore session state from OPFS (HMAC key + active sessions)
        await RestoreSessionsFromOpfsAsync();

        // Ensure database tables exist (EF Core)
        await EnsureDatabaseAsync();

        var currentPath = JsInterop.GetCurrentPath();
        if (string.IsNullOrEmpty(currentPath))
            currentPath = defaultPath;

        await JsExports.Navigate(currentPath);

        var mvcEngine = _provider.GetRequiredService<IMvcEngine>() as MvcEngine;
        var signalR = _provider.GetRequiredService<ISignalREngine>();
        var routes = mvcEngine?.GetRoutes()?.Count ?? 0;
        var hubs = signalR.GetHubNames();

        JsInterop.ConsoleLog($"✅ Cepha ready — {routes} routes, {hubs.Count} hubs ({string.Join(", ", hubs)})");

        await Task.Delay(Timeout.Infinite);
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
                JsInterop.ConsoleLog($"🗄️ Database restored from OPFS ({bytes.Length:N0} bytes)");
            }
            else
            {
                JsInterop.ConsoleLog("🗄️ No database snapshot in OPFS — fresh start");
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
                JsInterop.ConsoleLog($"🗄️ Database persisted to OPFS ({bytes.Length:N0} bytes)");
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
                JsInterop.ConsoleLog("🔐 Sessions restored from OPFS");
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
            JsInterop.ConsoleLog($"🔐 Sessions persisted to OPFS ({json.Length} chars)");

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
                JsInterop.ConsoleLog("🗄️ Database tables ensured");
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
                JsInterop.ConsoleLog($"🧬 AtomicProbe: {userName} signed in [{string.Join(",", roles)}]");
                // Persist + broadcast immediately — all tabs get the new frame
                await PersistSessionsIfDirtyAsync();
            };

            triggers.OnSignedOut = async (userId, userName) =>
            {
                JsInterop.ConsoleLog($"🧬 AtomicProbe: {userName} signed out");
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

            JsInterop.ConsoleLog("🧬 Atomic Probes wired to IdentityTriggers");
        }
        catch { /* Identity not registered — no probes */ }
    }
}
