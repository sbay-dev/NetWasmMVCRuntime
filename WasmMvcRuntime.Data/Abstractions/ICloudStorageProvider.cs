namespace WasmMvcRuntime.Data.Abstractions;

/// <summary>
/// Cloud storage provider interface
/// </summary>
public interface ICloudStorageProvider
{
    /// <summary>
    /// Provider type
    /// </summary>
    CloudProvider Provider { get; }

    /// <summary>
    /// Provider name
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Is authenticated
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Authenticate with the cloud provider
    /// </summary>
    Task<bool> AuthenticateAsync();

    /// <summary>
    /// Upload file to cloud
    /// </summary>
    Task<UploadResult> UploadAsync(string fileName, byte[] data);

    /// <summary>
    /// Download file from cloud
    /// </summary>
    Task<byte[]?> DownloadAsync(string fileId);

    /// <summary>
    /// List files in cloud storage
    /// </summary>
    Task<List<CloudFile>> ListFilesAsync(string folder = "backups");

    /// <summary>
    /// Delete file from cloud
    /// </summary>
    Task<bool> DeleteAsync(string fileId);

    /// <summary>
    /// Get storage quota
    /// </summary>
    Task<StorageQuota> GetQuotaAsync();

    /// <summary>
    /// Sign out
    /// </summary>
    Task SignOutAsync();
}

/// <summary>
/// Cloud file information
/// </summary>
public record CloudFile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public string? Folder { get; init; }
}

/// <summary>
/// Storage quota information
/// </summary>
public record StorageQuota
{
    public long TotalSpace { get; init; }
    public long UsedSpace { get; init; }
    public long AvailableSpace => TotalSpace - UsedSpace;
    public double UsedPercentage => TotalSpace > 0 ? (double)UsedSpace / TotalSpace * 100 : 0;
}
