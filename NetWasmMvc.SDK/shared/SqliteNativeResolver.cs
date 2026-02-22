using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cepha;

/// <summary>
/// 🧬 Provides a fallback native library resolver for SQLite.
/// On non-browser platforms (e.g., Android/Termux proot, exotic Linux distros),
/// the bundled e_sqlite3 may not load. This resolver falls back to the system's
/// libsqlite3 when the bundled library is unavailable.
/// </summary>
internal static class SqliteNativeResolver
{
    private static bool _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // WASM uses statically linked e_sqlite3.a — no dynamic loading needed
        if (OperatingSystem.IsBrowser()) return;

        try
        {
            // Register fallback resolver for the assembly containing SqliteConnection
            var sqliteAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SQLitePCLRaw.core");

            if (sqliteAssembly != null)
            {
                NativeLibrary.SetDllImportResolver(sqliteAssembly, ResolveSqliteNative);
            }
        }
        catch
        {
            // Resolver already set or not supported — use default behavior
        }

        // Also register via the event-based fallback (works alongside existing resolvers)
        try
        {
            System.Runtime.Loader.AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
        }
        catch
        {
            // Not supported on this platform
        }
    }

    private static IntPtr ResolveSqliteNative(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName is "e_sqlite3" or "libe_sqlite3")
        {
            return TryLoadSqliteFallbacks(libraryName, assembly, searchPath);
        }

        return IntPtr.Zero;
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        if (libraryName is "e_sqlite3" or "libe_sqlite3")
        {
            return TryLoadSqliteFallbacks(libraryName, null, null);
        }

        return IntPtr.Zero;
    }

    private static IntPtr TryLoadSqliteFallbacks(string libraryName, Assembly? assembly, DllImportSearchPath? searchPath)
    {
        // 1. Try the original name first (works on most platforms)
        if (assembly != null && NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
            return handle;

        // 2. Fallback: system sqlite3 (apt install libsqlite3-dev)
        if (NativeLibrary.TryLoad("sqlite3", out handle))
            return handle;

        // 3. Fallback: versioned system library (common on Debian/Ubuntu)
        if (NativeLibrary.TryLoad("libsqlite3.so.0", out handle))
            return handle;

        // 4. Fallback: unversioned system library
        if (NativeLibrary.TryLoad("libsqlite3", out handle))
            return handle;

        // 5. Fallback: full path for common aarch64 location
        if (OperatingSystem.IsLinux())
        {
            foreach (var path in new[]
            {
                "/usr/lib/aarch64-linux-gnu/libsqlite3.so.0",
                "/usr/lib/aarch64-linux-gnu/libsqlite3.so",
                "/usr/lib/x86_64-linux-gnu/libsqlite3.so.0",
                "/usr/lib/libsqlite3.so.0",
                "/usr/lib/libsqlite3.so",
                "/lib/libsqlite3.so.0"
            })
            {
                if (NativeLibrary.TryLoad(path, out handle))
                    return handle;
            }
        }

        return IntPtr.Zero;
    }
}
