using Cepha.CLI.Services;

namespace Cepha.CLI.UI;

/// <summary>
/// Interactive console UI with colored output and arrow-key selection menus.
/// </summary>
internal static class ConsoleUI
{
    // ─── Branding Colors ─────────────────────────────────────
    private static readonly ConsoleColor Brand = ConsoleColor.Magenta;
    private static readonly ConsoleColor Success = ConsoleColor.Green;
    private static readonly ConsoleColor Warning = ConsoleColor.Yellow;
    private static readonly ConsoleColor Error = ConsoleColor.Red;
    private static readonly ConsoleColor Muted = ConsoleColor.DarkGray;
    private static readonly ConsoleColor Highlight = ConsoleColor.Cyan;

    // Cached update info (fetched once per session)
    private static UpdateChecker.UpdateInfo? _cliUpdate;
    private static bool _updateChecked;

    public static void Banner()
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Brand;
        Console.WriteLine();
        var version = typeof(ConsoleUI).Assembly.GetName().Version;
        Console.WriteLine($"  🧬 Cepha CLI v{version?.ToString(3) ?? "0.0.0"}");
        Console.WriteLine("  Powered by NetWasmMvc.SDK");
        Console.ForegroundColor = Muted;
        Console.WriteLine("  ─────────────────────────");
        Console.ForegroundColor = old;

        // Non-blocking update check (fire once per session)
        if (!_updateChecked)
        {
            _updateChecked = true;
            try
            {
                var task = UpdateChecker.CheckCliAsync();
                if (task.Wait(TimeSpan.FromSeconds(3)))
                {
                    _cliUpdate = task.Result;
                    if (_cliUpdate.UpdateAvailable)
                    {
                        Console.ForegroundColor = Warning;
                        Console.WriteLine($"  ⬆️  Update available: v{_cliUpdate.LatestVersion} (current: v{_cliUpdate.CurrentVersion})");
                        Console.ForegroundColor = Muted;
                        Console.WriteLine("     Run: dotnet tool update --global Cepha.CLI");
                        Console.ForegroundColor = old;
                    }
                }
            }
            catch { }
        }
        else if (_cliUpdate?.UpdateAvailable == true)
        {
            Console.ForegroundColor = Warning;
            Console.WriteLine($"  ⬆️  Update available: v{_cliUpdate.LatestVersion}");
            Console.ForegroundColor = old;
        }

        Console.WriteLine();
    }

    public static void WriteSuccess(string msg)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Success;
        Console.Write("  ✅ ");
        Console.ForegroundColor = old;
        Console.WriteLine(msg);
    }

    public static void WriteError(string msg)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Error;
        Console.Write("  ❌ ");
        Console.ForegroundColor = old;
        Console.WriteLine(msg);
    }

    public static void WriteWarning(string msg)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Warning;
        Console.Write("  ⚠️  ");
        Console.ForegroundColor = old;
        Console.WriteLine(msg);
    }

    public static void WriteInfo(string msg)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Highlight;
        Console.Write("  🧬 ");
        Console.ForegroundColor = old;
        Console.WriteLine(msg);
    }

    public static void WriteStep(string msg)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Muted;
        Console.Write("  → ");
        Console.ForegroundColor = old;
        Console.WriteLine(msg);
    }

    // ─── Interactive Arrow-Key Menu ──────────────────────────

    public static int Select(string prompt, string[] options)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Highlight;
        Console.WriteLine($"  {prompt}");
        Console.ForegroundColor = old;
        Console.WriteLine();

        int selected = 0;
        bool done = false;

        Console.CursorVisible = false;
        try
        {
            // Draw items once to establish buffer space, then get top
            for (int i = 0; i < options.Length; i++)
                Console.WriteLine();
            var top = Console.CursorTop - options.Length;

            while (!done)
            {
                Console.SetCursorPosition(0, top);
                for (int i = 0; i < options.Length; i++)
                {
                    // Clear the entire line first
                    Console.Write(new string(' ', Console.BufferWidth - 1));
                    Console.SetCursorPosition(0, top + i);

                    if (i == selected)
                    {
                        Console.ForegroundColor = Brand;
                        Console.Write("  ❯ ");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine(options[i]);
                    }
                    else
                    {
                        Console.ForegroundColor = Muted;
                        Console.Write("    ");
                        Console.WriteLine(options[i]);
                    }
                }
                Console.ForegroundColor = old;

                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selected = selected > 0 ? selected - 1 : options.Length - 1;
                        break;
                    case ConsoleKey.DownArrow:
                        selected = selected < options.Length - 1 ? selected + 1 : 0;
                        break;
                    case ConsoleKey.Enter:
                        done = true;
                        break;
                    case ConsoleKey.Escape:
                        return -1;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }

        Console.WriteLine();
        return selected;
    }

    // ─── Progress Spinner ────────────────────────────────────

    public static async Task<T> WithSpinner<T>(string message, Func<Task<T>> action)
    {
        var spinChars = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
        int i = 0;
        var old = Console.ForegroundColor;
        var cts = new CancellationTokenSource();

        Console.CursorVisible = false;
        var spinnerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Console.ForegroundColor = Brand;
                Console.Write($"\r  {spinChars[i++ % spinChars.Length]} ");
                Console.ForegroundColor = old;
                Console.Write(message);
                try { await Task.Delay(80, cts.Token); } catch { break; }
            }
        });

        try
        {
            var result = await action();
            cts.Cancel();
            await spinnerTask;
            Console.Write($"\r  {new string(' ', message.Length + 6)}\r");
            Console.CursorVisible = true;
            return result;
        }
        catch
        {
            cts.Cancel();
            await spinnerTask;
            Console.CursorVisible = true;
            throw;
        }
    }

    public static async Task WithSpinner(string message, Func<Task> action)
    {
        await WithSpinner(message, async () => { await action(); return 0; });
    }

    // ─── Text Input ──────────────────────────────────────────

    public static string Prompt(string label, string? defaultValue = null)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Highlight;
        Console.Write($"  {label}");
        if (defaultValue != null)
        {
            Console.ForegroundColor = Muted;
            Console.Write($" ({defaultValue})");
        }
        Console.ForegroundColor = old;
        Console.Write(": ");
        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) ? (defaultValue ?? "") : input;
    }

    public static bool Confirm(string question, bool defaultYes = true)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = Highlight;
        Console.Write($"  {question}");
        Console.ForegroundColor = Muted;
        Console.Write(defaultYes ? " [Y/n]: " : " [y/N]: ");
        Console.ForegroundColor = old;
        var key = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key)) return defaultYes;
        return key is "y" or "yes";
    }
}
