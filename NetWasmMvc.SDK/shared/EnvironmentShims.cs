using System;
using System.Collections.Concurrent;

/// <summary>
/// Browser-safe Environment shim for app code using unqualified Environment.* calls.
/// Keeps startup code unchanged while avoiding PlatformNotSupported on browser-wasm.
/// </summary>
public static class Environment
{
    private static readonly ConcurrentDictionary<string, string?> _variables =
        new(StringComparer.OrdinalIgnoreCase);

    public static void SetEnvironmentVariable(string variable, string? value)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            throw new ArgumentException("Variable name cannot be null or empty.", nameof(variable));
        }

        if (value is null)
        {
            _variables.TryRemove(variable, out _);
            return;
        }

        _variables[variable] = value;
    }

    public static string? GetEnvironmentVariable(string variable)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            throw new ArgumentException("Variable name cannot be null or empty.", nameof(variable));
        }

        return _variables.TryGetValue(variable, out var value) ? value : null;
    }

    public static long TickCount64
    {
        get
        {
            try
            {
                return System.Environment.TickCount64;
            }
            catch (PlatformNotSupportedException)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
    }
}
