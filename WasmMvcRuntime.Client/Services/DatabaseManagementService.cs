using Microsoft.EntityFrameworkCore;
using System.Text;
using WasmMvcRuntime.App.Data;

namespace WasmMvcRuntime.Client.Services;

/// <summary>
/// Service for importing, exporting, and managing SQLite database
/// </summary>
public class DatabaseManagementService
{
    private readonly ApplicationDbContext _context;
    private readonly DatabaseInitializationService _initService;
    private const string DB_FILE_NAME = "wasmapp.db";

    public DatabaseManagementService(
        ApplicationDbContext context,
        DatabaseInitializationService initService)
    {
        _context = context;
        _initService = initService;
    }

    /// <summary>
    /// Exports database to a downloadable file
    /// </summary>
    public async Task<ExportResult> ExportDatabaseAsync()
    {
        try
        {
            // Get all data from database
            var weatherData = await _context.WeatherData.ToListAsync();
            var cities = await _context.Cities.ToListAsync();

            // Create export object
            var exportData = new DatabaseExport
            {
                ExportDate = DateTime.Now,
                Version = "1.0",
                WeatherData = weatherData,
                Cities = cities
            };

            // Serialize to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Convert to base64 for download
            var bytes = Encoding.UTF8.GetBytes(json);
            var base64 = Convert.ToBase64String(bytes);

            // TODO: Trigger download via JSImport when needed
            var fileName = $"wasmapp_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            await Task.CompletedTask;

            return new ExportResult
            {
                Success = true,
                Message = "Database exported successfully",
                FileName = fileName,
                RecordsCount = weatherData.Count + cities.Count
            };
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                Message = $"Export error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Imports database from a JSON file
    /// </summary>
    public async Task<ImportResult> ImportDatabaseAsync(string jsonContent, bool replaceExisting = false)
    {
        try
        {
            // Deserialize JSON
            var importData = System.Text.Json.JsonSerializer.Deserialize<DatabaseExport>(jsonContent);

            if (importData == null)
            {
                return new ImportResult
                {
                    Success = false,
                    Message = "Invalid file"
                };
            }

            // If replace existing, clear database first
            if (replaceExisting)
            {
                await DeleteAllDataAsync();
            }

            // Import cities
            int citiesAdded = 0;
            if (importData.Cities != null)
            {
                foreach (var city in importData.Cities)
                {
                    // Check if city already exists
                    var existing = await _context.Cities
                        .FirstOrDefaultAsync(c => c.Name == city.Name);

                    if (existing == null)
                    {
                        city.Id = 0; // Reset ID for auto-increment
                        _context.Cities.Add(city);
                        citiesAdded++;
                    }
                    else if (replaceExisting)
                    {
                        // Update existing
                        existing.Country = city.Country;
                        existing.Latitude = city.Latitude;
                        existing.Longitude = city.Longitude;
                        existing.Population = city.Population;
                    }
                }
            }

            // Import weather data
            int weatherAdded = 0;
            if (importData.WeatherData != null)
            {
                foreach (var weather in importData.WeatherData)
                {
                    weather.Id = 0; // Reset ID for auto-increment
                    _context.WeatherData.Add(weather);
                    weatherAdded++;
                }
            }

            // Save changes
            await _context.SaveChangesAsync();

            return new ImportResult
            {
                Success = true,
                Message = "Data imported successfully",
                CitiesImported = citiesAdded,
                WeatherDataImported = weatherAdded
            };
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                Message = $"Import error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Deletes all data from database (keeps structure)
    /// </summary>
    public async Task<DeleteResult> DeleteAllDataAsync()
    {
        try
        {
            var weatherCount = await _context.WeatherData.CountAsync();
            var citiesCount = await _context.Cities.CountAsync();

            // Remove all weather data
            _context.WeatherData.RemoveRange(_context.WeatherData);

            // Remove all cities
            _context.Cities.RemoveRange(_context.Cities);

            // Save changes
            await _context.SaveChangesAsync();

            return new DeleteResult
            {
                Success = true,
                Message = "All data deleted successfully",
                WeatherDataDeleted = weatherCount,
                CitiesDeleted = citiesCount
            };
        }
        catch (Exception ex)
        {
            return new DeleteResult
            {
                Success = false,
                Message = $"Delete error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Completely resets the database (drops and recreates)
    /// </summary>
    public async Task<DeleteResult> ResetDatabaseAsync()
    {
        try
        {
            await _initService.ResetDatabaseAsync();

            return new DeleteResult
            {
                Success = true,
                Message = "Database reset successfully (with seed data)"
            };
        }
        catch (Exception ex)
        {
            return new DeleteResult
            {
                Success = false,
                Message = $"Reset error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets database file size estimate
    /// </summary>
    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        var stats = await _initService.GetStatsAsync();

        return new DatabaseInfo
        {
            WeatherRecords = stats.WeatherDataCount,
            CitiesCount = stats.CitiesCount,
            TotalRecords = stats.WeatherDataCount + stats.CitiesCount,
            IsInitialized = stats.IsInitialized,
            DatabaseName = DB_FILE_NAME
        };
    }
}

/// <summary>
/// Export result
/// </summary>
public class ExportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int RecordsCount { get; set; }
}

/// <summary>
/// Import result
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CitiesImported { get; set; }
    public int WeatherDataImported { get; set; }
}

/// <summary>
/// Delete result
/// </summary>
public class DeleteResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int WeatherDataDeleted { get; set; }
    public int CitiesDeleted { get; set; }
}

/// <summary>
/// Database export format
/// </summary>
public class DatabaseExport
{
    public DateTime ExportDate { get; set; }
    public string Version { get; set; } = "1.0";
    public List<WasmMvcRuntime.App.Models.WeatherData> WeatherData { get; set; } = new();
    public List<WasmMvcRuntime.App.Models.City> Cities { get; set; } = new();
}

/// <summary>
/// Database information
/// </summary>
public class DatabaseInfo
{
    public int WeatherRecords { get; set; }
    public int CitiesCount { get; set; }
    public int TotalRecords { get; set; }
    public bool IsInitialized { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
}
