using Microsoft.Extensions.DependencyInjection;
using WasmMvcRuntime.Data.Abstractions;
using WasmMvcRuntime.Data.Providers;
using WasmMvcRuntime.Data.Services;
using WasmMvcRuntime.Data.CloudProviders;

namespace WasmMvcRuntime.Data.Extensions;

/// <summary>
/// Service collection extensions for WasmMvcRuntime.Data
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add WasmMvcRuntime.Data services
    /// </summary>
    public static IServiceCollection AddWasmDataManagement(
        this IServiceCollection services,
        Action<DataManagementOptions>? configure = null)
    {
        var options = new DataManagementOptions();
        configure?.Invoke(options);

        // Register data provider
        if (options.DataProviderType != null)
        {
            services.AddScoped(typeof(IDataProvider), options.DataProviderType);
        }
        else
        {
            // Default to SQLite
            services.AddScoped<IDataProvider, SQLiteDataProvider>();
        }

        // Register SQLite configuration
        services.AddSingleton(options.SQLiteConfiguration ?? new SQLiteConfiguration());

        // Register backup service
        services.AddScoped<IBackupService>(sp =>
        {
            var dataProvider = sp.GetRequiredService<IDataProvider>();
            var cloudProviders = sp.GetServices<ICloudStorageProvider>();
            
            return new BackupService(
                dataProvider,
                cloudProviders,
                options.BackupDirectory ?? "backups"
            );
        });

        // Register cloud providers
        foreach (var cloudProviderType in options.CloudProviderTypes)
        {
            services.AddScoped(typeof(ICloudStorageProvider), cloudProviderType);
        }

        // Register OneDrive configuration if provided
        if (options.OneDriveConfiguration != null)
        {
            services.AddSingleton(options.OneDriveConfiguration);
        }

        return services;
    }

    /// <summary>
    /// Add SQLite data provider
    /// </summary>
    public static DataManagementOptions UseSQLite(
        this DataManagementOptions options,
        Action<SQLiteConfiguration>? configure = null)
    {
        var config = new SQLiteConfiguration();
        configure?.Invoke(config);
        
        options.DataProviderType = typeof(SQLiteDataProvider);
        options.SQLiteConfiguration = config;
        
        return options;
    }

    /// <summary>
    /// Add OneDrive cloud storage
    /// </summary>
    public static DataManagementOptions UseOneDrive(
        this DataManagementOptions options,
        Action<OneDriveConfiguration>? configure = null)
    {
        var config = new OneDriveConfiguration();
        configure?.Invoke(config);
        
        options.CloudProviderTypes.Add(typeof(OneDriveProvider));
        options.OneDriveConfiguration = config;
        
        return options;
    }

    /// <summary>
    /// Configure backup settings
    /// </summary>
    public static DataManagementOptions ConfigureBackup(
        this DataManagementOptions options,
        string backupDirectory)
    {
        options.BackupDirectory = backupDirectory;
        return options;
    }
}

/// <summary>
/// Data management configuration options
/// </summary>
public class DataManagementOptions
{
    public Type? DataProviderType { get; set; }
    public SQLiteConfiguration? SQLiteConfiguration { get; set; }
    public string? BackupDirectory { get; set; }
    public List<Type> CloudProviderTypes { get; set; } = new();
    public OneDriveConfiguration? OneDriveConfiguration { get; set; }
}
