using System.IO.Compression;
using System.Text.Json;
using WasmMvcRuntime.Data.Abstractions;

namespace WasmMvcRuntime.Data.Services;

/// <summary>
/// Backup service implementation
/// </summary>
public class BackupService : IBackupService
{
    private readonly IDataProvider _dataProvider;
    private readonly IEnumerable<ICloudStorageProvider> _cloudProviders;
    private readonly string _backupDirectory;
    private readonly string _metadataFile;

    public BackupService(
        IDataProvider dataProvider,
        IEnumerable<ICloudStorageProvider> cloudProviders,
        string backupDirectory = "backups")
    {
        _dataProvider = dataProvider;
        _cloudProviders = cloudProviders;
        _backupDirectory = backupDirectory;
        _metadataFile = Path.Combine(_backupDirectory, "backups.json");

        // Ensure backup directory exists
        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<BackupResult> CreateBackupAsync(BackupOptions options)
    {
        try
        {
            // Export database
            var data = await _dataProvider.ExportAsync();
            
            // Get stats for metadata
            var stats = await _dataProvider.GetStatsAsync();

            // Compress if requested
            if (options.Compress)
            {
                data = await CompressAsync(data);
            }

            // Encrypt if requested
            if (options.Encrypt && !string.IsNullOrEmpty(options.EncryptionKey))
            {
                // TODO: Implement encryption
                // data = await EncryptAsync(data, options.EncryptionKey);
            }

            // Generate backup ID
            var backupId = Guid.NewGuid().ToString();
            var fileName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}_{backupId}.bak";
            var filePath = Path.Combine(_backupDirectory, fileName);

            // Save backup file
            await File.WriteAllBytesAsync(filePath, data);

            // Create metadata
            var metadata = new BackupMetadata
            {
                Id = backupId,
                CreatedAt = DateTime.UtcNow,
                Type = BackupType.Full,
                Size = data.Length,
                IsCompressed = options.Compress,
                IsEncrypted = options.Encrypt,
                Description = options.Description,
                LocalPath = filePath,
                TableCounts = stats.TableCounts
            };

            // Save metadata
            await SaveBackupMetadataAsync(metadata);

            return new BackupResult
            {
                Success = true,
                BackupId = backupId,
                Message = $"Backup created successfully",
                BackupSize = data.Length
            };
        }
        catch (Exception ex)
        {
            return new BackupResult
            {
                Success = false,
                Message = $"Failed to create backup: {ex.Message}"
            };
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupId)
    {
        try
        {
            // Get backup metadata
            var metadata = await GetBackupInfoAsync(backupId);
            if (metadata == null)
            {
                return new RestoreResult
                {
                    Success = false,
                    Message = "Backup not found"
                };
            }

            if (string.IsNullOrEmpty(metadata.LocalPath) || !File.Exists(metadata.LocalPath))
            {
                return new RestoreResult
                {
                    Success = false,
                    Message = "Backup file not found"
                };
            }

            // Read backup file
            var data = await File.ReadAllBytesAsync(metadata.LocalPath);

            // Decrypt if needed
            if (metadata.IsEncrypted)
            {
                // TODO: Implement decryption
                // data = await DecryptAsync(data, encryptionKey);
            }

            // Decompress if needed
            if (metadata.IsCompressed)
            {
                data = await DecompressAsync(data);
            }

            // Import to database
            await _dataProvider.ImportAsync(data);

            var totalRecords = metadata.TableCounts.Values.Sum();

            return new RestoreResult
            {
                Success = true,
                Message = "Backup restored successfully",
                RecordsRestored = totalRecords
            };
        }
        catch (Exception ex)
        {
            return new RestoreResult
            {
                Success = false,
                Message = $"Failed to restore backup: {ex.Message}"
            };
        }
    }

    public async Task<List<BackupMetadata>> ListBackupsAsync()
    {
        try
        {
            if (!File.Exists(_metadataFile))
            {
                return new List<BackupMetadata>();
            }

            var json = await File.ReadAllTextAsync(_metadataFile);
            var backups = JsonSerializer.Deserialize<List<BackupMetadata>>(json);
            
            return backups ?? new List<BackupMetadata>();
        }
        catch
        {
            return new List<BackupMetadata>();
        }
    }

    public async Task<bool> DeleteBackupAsync(string backupId)
    {
        try
        {
            var metadata = await GetBackupInfoAsync(backupId);
            if (metadata == null)
            {
                return false;
            }

            // Delete backup file
            if (!string.IsNullOrEmpty(metadata.LocalPath) && File.Exists(metadata.LocalPath))
            {
                File.Delete(metadata.LocalPath);
            }

            // Remove from metadata
            var backups = await ListBackupsAsync();
            backups.RemoveAll(b => b.Id == backupId);
            await SaveBackupsListAsync(backups);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<UploadResult> UploadToCloudAsync(string backupId, CloudProvider provider)
    {
        try
        {
            // Get backup metadata
            var metadata = await GetBackupInfoAsync(backupId);
            if (metadata == null)
            {
                return new UploadResult
                {
                    Success = false,
                    Message = "Backup not found"
                };
            }

            // Get cloud provider
            var cloudProvider = _cloudProviders.FirstOrDefault(p => p.Provider == provider);
            if (cloudProvider == null)
            {
                return new UploadResult
                {
                    Success = false,
                    Message = "Cloud provider not available"
                };
            }

            // Authenticate if needed
            if (!cloudProvider.IsAuthenticated)
            {
                var authenticated = await cloudProvider.AuthenticateAsync();
                if (!authenticated)
                {
                    return new UploadResult
                    {
                        Success = false,
                        Message = "Authentication with cloud provider failed"
                    };
                }
            }

            // Read backup file
            var data = await File.ReadAllBytesAsync(metadata.LocalPath!);

            // Upload
            var fileName = Path.GetFileName(metadata.LocalPath);
            var result = await cloudProvider.UploadAsync(fileName, data);

            if (result.Success)
            {
                // Update metadata
                if (!metadata.CloudProviders.Contains(provider))
                {
                    metadata.CloudProviders.Add(provider);
                }
                metadata = metadata with { CloudFileId = result.FileId };
                await SaveBackupMetadataAsync(metadata);
            }

            return result;
        }
        catch (Exception ex)
        {
            return new UploadResult
            {
                Success = false,
                Message = $"Upload failed: {ex.Message}"
            };
        }
    }

    public async Task<DownloadResult> DownloadFromCloudAsync(string cloudBackupId, CloudProvider provider)
    {
        try
        {
            // Get cloud provider
            var cloudProvider = _cloudProviders.FirstOrDefault(p => p.Provider == provider);
            if (cloudProvider == null)
            {
                return new DownloadResult
                {
                    Success = false,
                    Message = "Cloud provider not available"
                };
            }

            // Download
            var data = await cloudProvider.DownloadAsync(cloudBackupId);
            
            if (data == null)
            {
                return new DownloadResult
                {
                    Success = false,
                    Message = "Download from cloud failed"
                };
            }

            return new DownloadResult
            {
                Success = true,
                Data = data,
                Message = "Downloaded successfully"
            };
        }
        catch (Exception ex)
        {
            return new DownloadResult
            {
                Success = false,
                Message = $"Download failed: {ex.Message}"
            };
        }
    }

    public async Task<BackupMetadata?> GetBackupInfoAsync(string backupId)
    {
        var backups = await ListBackupsAsync();
        return backups.FirstOrDefault(b => b.Id == backupId);
    }

    private async Task SaveBackupMetadataAsync(BackupMetadata metadata)
    {
        var backups = await ListBackupsAsync();
        
        // Remove existing if any
        backups.RemoveAll(b => b.Id == metadata.Id);
        
        // Add new
        backups.Add(metadata);
        
        // Save
        await SaveBackupsListAsync(backups);
    }

    private async Task SaveBackupsListAsync(List<BackupMetadata> backups)
    {
        var json = JsonSerializer.Serialize(backups, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        await File.WriteAllTextAsync(_metadataFile, json);
    }

    private async Task<byte[]> CompressAsync(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            await gzip.WriteAsync(data);
        }
        return output.ToArray();
    }

    private async Task<byte[]> DecompressAsync(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        {
            await gzip.CopyToAsync(output);
        }
        return output.ToArray();
    }
}
