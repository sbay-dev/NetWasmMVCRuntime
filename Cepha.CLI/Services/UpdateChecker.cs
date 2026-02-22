using System.Net.Http.Json;
using System.Text.Json;

namespace Cepha.CLI.Services;

/// <summary>
/// Checks NuGet for latest versions of Cepha.CLI and NetWasmMvc.SDK.
/// </summary>
internal static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private const string CliPackageId = "Cepha.CLI";
    private const string SdkPackageId = "NetWasmMvc.SDK";

    public record UpdateInfo(string PackageId, string CurrentVersion, string? LatestVersion, bool UpdateAvailable);

    /// <summary>Gets the latest stable version of a NuGet package.</summary>
    public static async Task<string?> GetLatestVersionAsync(string packageId)
    {
        try
        {
            var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
            var json = await Http.GetFromJsonAsync<JsonElement>(url);
            if (json.TryGetProperty("versions", out var versions))
            {
                string? latest = null;
                foreach (var v in versions.EnumerateArray())
                {
                    var ver = v.GetString();
                    if (ver != null && !ver.Contains('-'))
                        latest = ver;
                }
                return latest;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Check CLI update availability.</summary>
    public static async Task<UpdateInfo> CheckCliAsync()
    {
        var current = typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var latest = await GetLatestVersionAsync(CliPackageId);
        var updateAvailable = latest != null && CompareVersions(latest, current) > 0;
        return new UpdateInfo(CliPackageId, current, latest, updateAvailable);
    }

    /// <summary>Check SDK update for a project.</summary>
    public static async Task<UpdateInfo> CheckSdkAsync(string? currentSdkVersion = null)
    {
        var current = currentSdkVersion ?? "unknown";
        var latest = await GetLatestVersionAsync(SdkPackageId);
        var updateAvailable = latest != null && current != "unknown" && CompareVersions(latest, current) > 0;
        return new UpdateInfo(SdkPackageId, current, latest, updateAvailable);
    }

    /// <summary>Check both CLI and SDK in parallel.</summary>
    public static async Task<(UpdateInfo Cli, UpdateInfo Sdk)> CheckAllAsync(string? currentSdkVersion = null)
    {
        var cliTask = CheckCliAsync();
        var sdkTask = CheckSdkAsync(currentSdkVersion);
        await Task.WhenAll(cliTask, sdkTask);
        return (cliTask.Result, sdkTask.Result);
    }

    /// <summary>Simple semver comparison: returns >0 if a > b.</summary>
    internal static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var pb = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }
}
