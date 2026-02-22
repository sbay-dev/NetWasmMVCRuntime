using WasmMvcRuntime.Abstractions;

namespace MyCephaApp.Controllers;

public class HomeController : Controller
{
    [Route("/")]
    [Route("/home")]
    [Route("/home/index")]
    public ViewResult Index()
    {
        ViewBag["Title"] = "Welcome to Cepha!";
        ViewBag["Message"] = "This app runs entirely in WebAssembly — no server required.";
        return View();
    }

    [Route("/home/about")]
    public ViewResult About()
    {
        ViewBag["Title"] = "About";
        ViewBag["Message"] = "Built with NetWasmMvc.SDK — the first MVC framework for WebAssembly.";
        return View();
    }
}
