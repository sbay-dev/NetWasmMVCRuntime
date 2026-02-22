using System.Net.Http.Headers;
using System.Text.Json;
using WasmMvcRuntime.Data.Abstractions;

namespace WasmMvcRuntime.Data.CloudProviders;

/// <summary>
/// OneDrive cloud storage provider using Microsoft Graph API
/// </summary>
public class OneDriveProvider : ICloudStorageProvider
{
    private readonly HttpClient _httpClient;
    private readonly OneDriveConfiguration _config;
    private string? _accessToken;

    public CloudProvider Provider => CloudProvider.OneDrive;
    public string ProviderName => "OneDrive";
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    public OneDriveProvider(HttpClient httpClient, OneDriveConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            // For Blazor WASM, we need to use OAuth 2.0 implicit flow
            // This should be initiated from JavaScript
            
            // In a real implementation, you would:
            // 1. Open auth window using JS Interop
            // 2. Get access token from callback
            // 3. Store token
            
            // For now, we'll assume the token is provided via configuration
            // or obtained through a separate auth flow
            
            if (!string.IsNullOrEmpty(_config.AccessToken))
            {
                _accessToken = _config.AccessToken;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<UploadResult> UploadAsync(string fileName, byte[] data)
    {
        try
        {
            if (!IsAuthenticated)
            {
                return new UploadResult
                {
                    Success = false,
                    Message = "Not authenticated"
                };
            }

            // Microsoft Graph API endpoint for file upload
            var url = $"https://graph.microsoft.com/v1.0/me/drive/root:/backups/{fileName}:/content";

            using var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.PutAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OneDriveFileResponse>(json);

                return new UploadResult
                {
                    Success = true,
                    FileId = result?.Id,
                    Message = "Upload successful"
                };
            }

            var error = await response.Content.ReadAsStringAsync();
            return new UploadResult
            {
                Success = false,
                Message = $"Upload failed: {response.StatusCode} - {error}"
            };
        }
        catch (Exception ex)
        {
            return new UploadResult
            {
                Success = false,
                Message = $"Upload error: {ex.Message}"
            };
        }
    }

    public async Task<byte[]?> DownloadAsync(string fileId)
    {
        try
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            // Get download URL
            var url = $"https://graph.microsoft.com/v1.0/me/drive/items/{fileId}/content";

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<CloudFile>> ListFilesAsync(string folder = "backups")
    {
        try
        {
            if (!IsAuthenticated)
            {
                return new List<CloudFile>();
            }

            var url = $"https://graph.microsoft.com/v1.0/me/drive/root:/{folder}:/children";

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OneDriveFilesResponse>(json);

                return result?.Value?.Select(f => new CloudFile
                {
                    Id = f.Id ?? string.Empty,
                    Name = f.Name ?? string.Empty,
                    Size = f.Size,
                    CreatedAt = f.CreatedDateTime,
                    ModifiedAt = f.LastModifiedDateTime,
                    Folder = folder
                }).ToList() ?? new List<CloudFile>();
            }

            return new List<CloudFile>();
        }
        catch
        {
            return new List<CloudFile>();
        }
    }

    public async Task<bool> DeleteAsync(string fileId)
    {
        try
        {
            if (!IsAuthenticated)
            {
                return false;
            }

            var url = $"https://graph.microsoft.com/v1.0/me/drive/items/{fileId}";

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.DeleteAsync(url);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<StorageQuota> GetQuotaAsync()
    {
        try
        {
            if (!IsAuthenticated)
            {
                return new StorageQuota();
            }

            var url = "https://graph.microsoft.com/v1.0/me/drive";

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OneDriveDriveResponse>(json);

                return new StorageQuota
                {
                    TotalSpace = result?.Quota?.Total ?? 0,
                    UsedSpace = result?.Quota?.Used ?? 0
                };
            }

            return new StorageQuota();
        }
        catch
        {
            return new StorageQuota();
        }
    }

    public Task SignOutAsync()
    {
        _accessToken = null;
        return Task.CompletedTask;
    }
}

/// <summary>
/// OneDrive configuration
/// </summary>
public record OneDriveConfiguration
{
    public string? ClientId { get; init; }
    public string? RedirectUri { get; init; }
    public string? AccessToken { get; init; }
}

// DTOs for OneDrive API responses
internal record OneDriveFileResponse
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public long Size { get; init; }
}

internal record OneDriveFilesResponse
{
    public List<OneDriveFileItem>? Value { get; init; }
}

internal record OneDriveFileItem
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public long Size { get; init; }
    public DateTime CreatedDateTime { get; init; }
    public DateTime LastModifiedDateTime { get; init; }
}

internal record OneDriveDriveResponse
{
    public OneDriveQuota? Quota { get; init; }
}

internal record OneDriveQuota
{
    public long Total { get; init; }
    public long Used { get; init; }
}
