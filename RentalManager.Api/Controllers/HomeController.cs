using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RentalManager.Api.Controllers;

[AllowAnonymous]
public sealed class HomeController : Controller
{
    public IActionResult Index()
    {
        ViewData["AppVersion"] = GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return View();
    }
}
