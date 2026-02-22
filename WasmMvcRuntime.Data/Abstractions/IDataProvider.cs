using Microsoft.EntityFrameworkCore;

namespace WasmMvcRuntime.Data.Abstractions;

/// <summary>
/// Database provider interface
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// Provider name
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Database type
    /// </summary>
    DatabaseType Type { get; }

    /// <summary>
    /// Initialize the database
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Get database context
    /// </summary>
    Task<DbContext> GetDbContextAsync();

    /// <summary>
    /// Check database health
    /// </summary>
    Task<bool> CheckHealthAsync();

    /// <summary>
    /// Get database statistics
    /// </summary>
    Task<DatabaseStats> GetStatsAsync();

    /// <summary>
    /// Run migrations
    /// </summary>
    Task MigrateAsync();

    /// <summary>
    /// Export database to bytes
    /// </summary>
    Task<byte[]> ExportAsync();

    /// <summary>
    /// Import database from bytes
    /// </summary>
    Task ImportAsync(byte[] data);

    /// <summary>
    /// Delete database
    /// </summary>
    Task DeleteAsync();

    /// <summary>
    /// Reset database (delete and recreate)
    /// </summary>
    Task ResetAsync();
}

/// <summary>
/// Database type enumeration
/// </summary>
public enum DatabaseType
{
    SQLite,
    IndexedDB,
    InMemory,
    PostgreSQL,
    MySQL,
    SqlServer
}

/// <summary>
/// Database statistics
/// </summary>
public record DatabaseStats
{
    public int TotalTables { get; init; }
    public int TotalRecords { get; init; }
    public long DatabaseSize { get; init; }
    public DateTime? LastBackup { get; init; }
    public bool IsHealthy { get; init; }
    public Dictionary<string, int> TableCounts { get; init; } = new();
}
