using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Services;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(
    RentalDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseReady = await db.Database.CanConnectAsync(ct);
        var configurationIssues = environment.IsDevelopment()
            ? []
            : ProductionConfigurationValidator.FindIssues(configuration);
        var ready = databaseReady && configurationIssues.Count == 0;
        return StatusCode(ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, new
        {
            status = ready ? "Healthy" : "Unhealthy",
            database = databaseReady ? "Connected" : "Disconnected",
            configuration = configurationIssues.Count == 0 ? "Ready" : "Incomplete",
            configurationIssueCount = configurationIssues.Count,
            utc = DateTime.UtcNow
        });
    }
}
