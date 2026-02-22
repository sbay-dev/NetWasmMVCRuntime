namespace WasmMvcRuntime.Data.Abstractions;

/// <summary>
/// Backup service interface
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Create a new backup
    /// </summary>
    Task<BackupResult> CreateBackupAsync(BackupOptions options);

    /// <summary>
    /// Restore from backup
    /// </summary>
    Task<RestoreResult> RestoreBackupAsync(string backupId);

    /// <summary>
    /// List all backups
    /// </summary>
    Task<List<BackupMetadata>> ListBackupsAsync();

    /// <summary>
    /// Delete a backup
    /// </summary>
    Task<bool> DeleteBackupAsync(string backupId);

    /// <summary>
    /// Upload backup to cloud
    /// </summary>
    Task<UploadResult> UploadToCloudAsync(string backupId, CloudProvider provider);

    /// <summary>
    /// Download backup from cloud
    /// </summary>
    Task<DownloadResult> DownloadFromCloudAsync(string cloudBackupId, CloudProvider provider);

    /// <summary>
    /// Get backup info
    /// </summary>
    Task<BackupMetadata?> GetBackupInfoAsync(string backupId);
}

/// <summary>
/// Backup options
/// </summary>
public record BackupOptions
{
    public bool IncludeData { get; init; } = true;
    public bool IncludeSchema { get; init; } = true;
    public bool Compress { get; init; } = true;
    public bool Encrypt { get; init; } = false;
    public string? EncryptionKey { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Backup metadata
/// </summary>
public record BackupMetadata
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public BackupType Type { get; init; }
    public long Size { get; init; }
    public bool IsCompressed { get; init; }
    public bool IsEncrypted { get; init; }
    public string? Description { get; init; }
    public string? LocalPath { get; init; }
    public string? CloudFileId { get; init; }
    public List<CloudProvider> CloudProviders { get; init; } = new();
    public Dictionary<string, int> TableCounts { get; init; } = new();
}

/// <summary>
/// Backup type
/// </summary>
public enum BackupType
{
    Full,
    Incremental,
    Differential
}

/// <summary>
/// Cloud provider enumeration
/// </summary>
public enum CloudProvider
{
    OneDrive,
    GoogleDrive,
    Dropbox,
    AzureBlob,
    AmazonS3
}

/// <summary>
/// Backup result
/// </summary>
public record BackupResult
{
    public bool Success { get; init; }
    public string? BackupId { get; init; }
    public string? Message { get; init; }
    public long BackupSize { get; init; }
}

/// <summary>
/// Restore result
/// </summary>
public record RestoreResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public int RecordsRestored { get; init; }
}

/// <summary>
/// Upload result
/// </summary>
public record UploadResult
{
    public bool Success { get; init; }
    public string? FileId { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Download result
/// </summary>
public record DownloadResult
{
    public bool Success { get; init; }
    public byte[]? Data { get; init; }
    public string? Message { get; init; }
}
