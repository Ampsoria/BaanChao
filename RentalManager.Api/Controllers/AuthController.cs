using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RentalManager.Api.Models;
using RentalManager.Api.Services;

namespace RentalManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var expectedUser = configuration["Admin:Username"];
        var passwordHash = configuration["Admin:PasswordHash"];
        var plaintextPassword = configuration["Admin:Password"];
        if (string.IsNullOrWhiteSpace(expectedUser) ||
            (string.IsNullOrWhiteSpace(passwordHash) && string.IsNullOrWhiteSpace(plaintextPassword)))
            return Problem("ยังไม่ได้ตั้ง Admin:Username และ Admin:PasswordHash", statusCode: StatusCodes.Status503ServiceUnavailable);

        // ใช้ hash ก่อนเสมอ ส่วน plaintext เก็บไว้เพื่อความเข้ากันได้กับ config เดิม
        var passwordMatches = AdminPasswordHasher.IsHash(passwordHash)
            ? AdminPasswordHasher.Verify(request.Password, passwordHash!)
            : !string.IsNullOrWhiteSpace(plaintextPassword) && FixedTimeEquals(request.Password, plaintextPassword);
        if (!FixedTimeEquals(request.Username, expectedUser) || !passwordMatches)
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
