using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(RentalDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseReady = await db.Database.CanConnectAsync(ct);
        return databaseReady
            ? Ok(new { status = "Healthy", database = "Connected", utc = DateTime.UtcNow })
            : StatusCode(503, new { status = "Unhealthy", database = "Disconnected", utc = DateTime.UtcNow });
    }
}
