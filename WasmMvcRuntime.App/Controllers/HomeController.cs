using System.Diagnostics;
using WasmMvcRuntime.Abstractions;
using WasmMvcRuntime.App.Models;

namespace WasmMvcRuntime.App.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
    public IActionResult Privacy() => View();
    public IActionResult ApiDocs() => View();
    public IActionResult Cepha() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier ?? "unknown" });
}
