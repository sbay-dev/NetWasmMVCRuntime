using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.Data.Abstractions;
using System.Text.Json;
using WasmMvcRuntime.Abstractions.Mvc;

namespace WasmMvcRuntime.Data.Providers;

/// <summary>
/// SQLite database provider
/// </summary>
public class SQLiteDataProvider : IDataProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SQLiteConfiguration _config;

    public string ProviderName => "SQLite";
    public DatabaseType Type => DatabaseType.SQLite;

    public SQLiteDataProvider(IServiceProvider serviceProvider, SQLiteConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
    }

    public async Task InitializeAsync()
    {
        var context = await GetDbContextAsync();
        
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();
        
        // Run pending migrations if any
        if (context.Database.GetPendingMigrations().Any())
        {
            await context.Database.MigrateAsync();
        }
    }

    public Task<DbContext> GetDbContextAsync()
    {
        var context = _serviceProvider.GetRequiredService<DbContext>();
        if (context == null)
        {
            throw new InvalidOperationException("DbContext not registered in service provider");
        }
        return Task.FromResult(context);
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var context = await GetDbContextAsync();
            return await context.Database.CanConnectAsync();
        }
        catch
        {
            return false;
        }
    }

    public async Task<DatabaseStats> GetStatsAsync()
    {
        try
        {
            var context = await GetDbContextAsync();
            
            // Get table counts
            var tableCounts = new Dictionary<string, int>();
            
            // Use reflection to get all DbSet properties
            var dbSetProperties = context.GetType()
                .GetProperties()
                .Where(p => p.PropertyType.IsGenericType && 
                           p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

            foreach (var property in dbSetProperties)
            {
                var dbSet = property.GetValue(context);
                if (dbSet != null)
                {
                    var countMethod = dbSet.GetType().GetMethod("CountAsync", new[] { typeof(CancellationToken) });
                    if (countMethod != null)
                    {
                        var countTask = (Task<int>)countMethod.Invoke(dbSet, new object[] { CancellationToken.None })!;
                        var count = await countTask;
                        tableCounts[property.Name] = count;
                    }
                }
            }

            var totalRecords = tableCounts.Values.Sum();
            var dbSize = await GetDatabaseSizeAsync();

            return new DatabaseStats
            {
                TotalTables = tableCounts.Count,
                TotalRecords = totalRecords,
                DatabaseSize = dbSize,
                IsHealthy = true,
                TableCounts = tableCounts
            };
        }
        catch
        {
            return new DatabaseStats
            {
                IsHealthy = false
            };
        }
    }

    public async Task MigrateAsync()
    {
        var context = await GetDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public async Task<byte[]> ExportAsync()
    {
        // Close all connections first
        var context = await GetDbContextAsync();
        await context.Database.CloseConnectionAsync();

        // Get database file path
        var dbPath = GetDatabasePath();
        
        if (!File.Exists(dbPath))
        {
            throw new FileNotFoundException("Database file not found", dbPath);
        }

        // Read database file
        var bytes = await File.ReadAllBytesAsync(dbPath);
        
        // Reopen connection
        await context.Database.OpenConnectionAsync();

        return bytes;
    }

    public async Task ImportAsync(byte[] data)
    {
        // Close all connections
        var context = await GetDbContextAsync();
        await context.Database.CloseConnectionAsync();

        // Get database path
        var dbPath = GetDatabasePath();
        
        // Backup existing database
        if (File.Exists(dbPath))
        {
            var backupPath = $"{dbPath}.backup_{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(dbPath, backupPath);
        }

        // Write new database
        await File.WriteAllBytesAsync(dbPath, data);

        // Reopen connection
        await context.Database.OpenConnectionAsync();
    }

    public async Task DeleteAsync()
    {
        var context = await GetDbContextAsync();
        await context.Database.EnsureDeletedAsync();
    }

    public async Task ResetAsync()
    {
        await DeleteAsync();
        await InitializeAsync();
    }

    private string GetDatabasePath()
    {
        return _config.DatabasePath;
    }

    private async Task<long> GetDatabaseSizeAsync()
    {
        try
        {
            var dbPath = GetDatabasePath();
            
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                return fileInfo.Length;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// SQLite configuration
/// </summary>
public record SQLiteConfiguration
{
    public string DatabasePath { get; init; } = "wasmapp.db";
    public bool EnableWAL { get; init; } = true;
    public int BusyTimeout { get; init; } = 3000;
    public string? ConnectionString { get; init; }
}
