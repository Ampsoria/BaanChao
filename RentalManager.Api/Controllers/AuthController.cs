using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalManager.Api.Models;

namespace RentalManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var expectedUser = configuration["Admin:Username"];
        var expectedPassword = configuration["Admin:Password"];
        if (string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedPassword))
            return Problem("ยังไม่ได้ตั้ง Admin:Username และ Admin:Password", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (!FixedTimeEquals(request.Username, expectedUser) || !FixedTimeEquals(request.Password, expectedPassword))
            return Unauthorized();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, expectedUser), new Claim(ClaimTypes.Role, "Admin")],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
        return Ok(new { username = expectedUser });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(new { username = User.Identity!.Name });

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        var suppliedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
