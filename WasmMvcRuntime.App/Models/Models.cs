using System.ComponentModel.DataAnnotations;

namespace WasmMvcRuntime.App.Models;

public class WeatherData
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Range(-50, 60)]
    public double Temperature { get; set; }

    [MaxLength(50)]
    public string? Condition { get; set; }

    [Range(0, 100)]
    public int Humidity { get; set; }

    [Range(0, 200)]
    public double WindSpeed { get; set; }

    public DateTime Date { get; set; } = DateTime.Now;

    public WeatherViewModel ToViewModel() => new()
    {
        City = City,
        Temperature = Temperature,
        Condition = Condition,
        Date = Date,
        Humidity = Humidity,
        WindSpeed = WindSpeed
    };
}

public class City
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Country { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public long? Population { get; set; }
}

public class WeatherViewModel
{
    public string? City { get; set; }
    public double Temperature { get; set; }
    public string? Condition { get; set; }
    public DateTime Date { get; set; }
    public int Humidity { get; set; }
    public double WindSpeed { get; set; }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

public class WeatherForecast
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
