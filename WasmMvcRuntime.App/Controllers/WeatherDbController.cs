using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.App.Models;
using WasmMvcRuntime.App.Repositories;

namespace WasmMvcRuntime.App.Controllers;

public class WeatherDbController : Controller
{
    private readonly IWeatherRepository _weatherRepo;
    private readonly ICityRepository _cityRepo;

    public WeatherDbController(IWeatherRepository weatherRepo, ICityRepository cityRepo)
    {
        _weatherRepo = weatherRepo;
        _cityRepo = cityRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var data = await _weatherRepo.GetAllAsync();
        var vms = data.Select(w => w.ToViewModel()).ToList();
        ViewData["Title"] = "Weather Database";
        ViewData["Count"] = vms.Count;
        return View(vms);
    }

    [HttpGet]
    public async Task<IActionResult> City(string name)
    {
        if (string.IsNullOrEmpty(name))
            return View("Error", new ErrorViewModel { RequestId = "City name is required" });
        var data = await _weatherRepo.GetByCityAsync(name);
        ViewData["Title"] = $"Weather for {name}";
        ViewData["CityName"] = name;
        return View("CityWeather", data.Select(w => w.ToViewModel()).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Recent(int count = 10)
        => Json((await _weatherRepo.GetRecentAsync(count)).Select(w => w.ToViewModel()).ToList());

    [HttpGet]
    public async Task<IActionResult> Cities()
    {
        var cities = await _cityRepo.GetAllAsync();
        ViewData["Title"] = "Cities";
        ViewData["Count"] = cities.Count();
        return View(cities);
    }

    [HttpPost]
    public async Task<IActionResult> Add(WeatherData weatherData)
    {
        if (weatherData == null) return Json(new { success = false, message = "Invalid data" });
        try
        {
            weatherData.Date = DateTime.Now;
            var r = await _weatherRepo.AddAsync(weatherData);
            return Json(new { success = true, id = r.Id, message = "Added" });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> Stats()
    {
        var wc = (await _weatherRepo.GetAllAsync()).Count();
        var cc = (await _cityRepo.GetAllAsync()).Count();
        return Json(new { WeatherRecords = wc, Cities = cc, LastUpdate = DateTime.Now });
    }
}
