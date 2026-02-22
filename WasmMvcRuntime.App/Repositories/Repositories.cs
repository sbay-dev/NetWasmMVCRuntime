using Microsoft.EntityFrameworkCore;
using WasmMvcRuntime.App.Data;
using WasmMvcRuntime.App.Models;

namespace WasmMvcRuntime.App.Repositories;

public interface IWeatherRepository
{
    Task<IEnumerable<WeatherData>> GetAllAsync();
    Task<WeatherData?> GetByIdAsync(int id);
    Task<IEnumerable<WeatherData>> GetByCityAsync(string city);
    Task<IEnumerable<WeatherData>> GetRecentAsync(int count = 10);
    Task<WeatherData> AddAsync(WeatherData weatherData);
    Task UpdateAsync(WeatherData weatherData);
    Task DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
}

public class WeatherRepository : IWeatherRepository
{
    private readonly ApplicationDbContext _ctx;
    public WeatherRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<IEnumerable<WeatherData>> GetAllAsync()
        => await _ctx.WeatherData.OrderByDescending(w => w.Date).ToListAsync();

    public async Task<WeatherData?> GetByIdAsync(int id)
        => await _ctx.WeatherData.FindAsync(id);

    public async Task<IEnumerable<WeatherData>> GetByCityAsync(string city)
        => await _ctx.WeatherData.Where(w => w.City == city).OrderByDescending(w => w.Date).ToListAsync();

    public async Task<IEnumerable<WeatherData>> GetRecentAsync(int count = 10)
        => await _ctx.WeatherData.OrderByDescending(w => w.Date).Take(count).ToListAsync();

    public async Task<WeatherData> AddAsync(WeatherData d)
    { _ctx.WeatherData.Add(d); await _ctx.SaveChangesAsync(); return d; }

    public async Task UpdateAsync(WeatherData d)
    { _ctx.Entry(d).State = EntityState.Modified; await _ctx.SaveChangesAsync(); }

    public async Task DeleteAsync(int id)
    { var e = await _ctx.WeatherData.FindAsync(id); if (e != null) { _ctx.WeatherData.Remove(e); await _ctx.SaveChangesAsync(); } }

    public async Task<bool> ExistsAsync(int id)
        => await _ctx.WeatherData.AnyAsync(e => e.Id == id);
}

public interface ICityRepository
{
    Task<IEnumerable<City>> GetAllAsync();
    Task<City?> GetByIdAsync(int id);
    Task<City?> GetByNameAsync(string name);
    Task<City> AddAsync(City city);
    Task UpdateAsync(City city);
    Task DeleteAsync(int id);
}

public class CityRepository : ICityRepository
{
    private readonly ApplicationDbContext _ctx;
    public CityRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<IEnumerable<City>> GetAllAsync()
        => await _ctx.Cities.OrderBy(c => c.Name).ToListAsync();

    public async Task<City?> GetByIdAsync(int id) => await _ctx.Cities.FindAsync(id);

    public async Task<City?> GetByNameAsync(string name)
        => await _ctx.Cities.FirstOrDefaultAsync(c => c.Name == name);

    public async Task<City> AddAsync(City c)
    { _ctx.Cities.Add(c); await _ctx.SaveChangesAsync(); return c; }

    public async Task UpdateAsync(City c)
    { _ctx.Entry(c).State = EntityState.Modified; await _ctx.SaveChangesAsync(); }

    public async Task DeleteAsync(int id)
    { var c = await _ctx.Cities.FindAsync(id); if (c != null) { _ctx.Cities.Remove(c); await _ctx.SaveChangesAsync(); } }
}
