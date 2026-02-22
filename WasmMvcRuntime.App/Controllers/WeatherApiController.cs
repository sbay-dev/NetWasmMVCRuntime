using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.App.Models;
using WasmMvcRuntime.App.Repositories;

namespace WasmMvcRuntime.App.Controllers;

[Route("api/[controller]")]
[ApiController]
public class WeatherApiController : ControllerBase
{
    private readonly IWeatherRepository _weatherRepo;
    private readonly ICityRepository _cityRepo;

    public WeatherApiController(IWeatherRepository weatherRepo, ICityRepository cityRepo)
    {
        _weatherRepo = weatherRepo;
        _cityRepo = cityRepo;
    }

    [HttpGet] public async Task<IActionResult> GetAll() => Ok(await _weatherRepo.GetAllAsync());

    [HttpGet]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _weatherRepo.GetByIdAsync(id);
        return item == null ? NotFound(new { error = "Not found", id }) : Ok(item);
    }

    [HttpGet] public async Task<IActionResult> GetByCity(string city) => Ok(await _weatherRepo.GetByCityAsync(city ?? ""));
    [HttpGet] public async Task<IActionResult> GetRecent(int count = 10) => Ok(await _weatherRepo.GetRecentAsync(count));

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var d = new WeatherData
        {
            City = "Riyadh",
            Temperature = Random.Shared.Next(20, 50),
            Condition = new[] { "Sunny", "Cloudy", "Windy", "Rainy", "Sandstorm" }[Random.Shared.Next(5)],
            Humidity = Random.Shared.Next(10, 80),
            WindSpeed = Random.Shared.Next(5, 40),
            Date = DateTime.Now
        };
        return Ok(new { success = true, data = await _weatherRepo.AddAsync(d) });
    }

    [HttpPut]
    public async Task<IActionResult> Update(int id)
    {
        var e = await _weatherRepo.GetByIdAsync(id);
        if (e == null) return NotFound(new { error = "Not found", id });
        e.Temperature = Random.Shared.Next(-10, 55);
        e.Date = DateTime.Now;
        await _weatherRepo.UpdateAsync(e);
        return Ok(new { success = true, data = e });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await _weatherRepo.ExistsAsync(id)) return NotFound(new { error = "Not found", id });
        await _weatherRepo.DeleteAsync(id);
        return Ok(new { success = true, message = $"Record {id} deleted" });
    }

    [HttpGet] public async Task<IActionResult> Cities() => Ok(await _cityRepo.GetAllAsync());

    [HttpGet]
    public async Task<IActionResult> Stats()
    {
        var w = await _weatherRepo.GetAllAsync();
        var c = await _cityRepo.GetAllAsync();
        return Ok(new { totalWeatherRecords = w.Count(), totalCities = c.Count(), timestamp = DateTime.Now });
    }
}
