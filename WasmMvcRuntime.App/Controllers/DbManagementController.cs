using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.App.Data;
using WasmMvcRuntime.App.Repositories;

namespace WasmMvcRuntime.App.Controllers;

public class DbManagementController : Controller
{
    private readonly ApplicationDbContext _context;

    public DbManagementController(ApplicationDbContext context) => _context = context;

    [HttpGet]
    public Task<IActionResult> Index()
    {
        ViewData["Title"] = "Database Management";
        return Task.FromResult<IActionResult>(View(GetInfo()));
    }

    [HttpGet]
    public async Task<IActionResult> Export()
    {
        var w = await new WeatherRepository(_context).GetAllAsync();
        var c = await new CityRepository(_context).GetAllAsync();
        return Json(new { weather = w, cities = c, exportedAt = DateTime.Now });
    }

    [HttpGet]
    public Task<IActionResult> Info() => Task.FromResult<IActionResult>(Json(GetInfo()));

    [HttpPost]
    public async Task<IActionResult> DeleteAll()
    {
        _context.WeatherData.RemoveRange(_context.WeatherData);
        _context.Cities.RemoveRange(_context.Cities);
        await _context.SaveChangesAsync();
        return Json(new { success = true, message = "All data deleted" });
    }

    [HttpPost]
    public async Task<IActionResult> Reset()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.Database.EnsureCreatedAsync();
        return Json(new { success = true, message = "Database reset with seed data" });
    }

    private object GetInfo() => new
    {
        weatherCount = _context.WeatherData.Count(),
        cityCount = _context.Cities.Count(),
        userCount = _context.Users.Count(),
        timestamp = DateTime.Now
    };
}
