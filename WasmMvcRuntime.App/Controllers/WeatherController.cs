using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.App.Models;
using WasmMvcRuntime.App.Repositories;

namespace WasmMvcRuntime.App.Controllers;

public class WeatherController : Controller
{
    private readonly IWeatherRepository _weatherRepo;
    private readonly ICityRepository _cityRepo;

    public WeatherController(IWeatherRepository weatherRepo, ICityRepository cityRepo)
    {
        _weatherRepo = weatherRepo;
        _cityRepo = cityRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var weatherData = await _weatherRepo.GetRecentAsync(20);
        var cities = await _cityRepo.GetAllAsync();
        ViewData["Cities"] = cities;
        ViewData["Title"] = "Weather";
        return View(weatherData.ToList());
    }
}
