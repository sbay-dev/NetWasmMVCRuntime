using WasmMvcRuntime.Abstractions;

namespace WasmMvcRuntime.App.Controllers;

public class ChatController : Controller
{
    public IActionResult Index() => View();
}
