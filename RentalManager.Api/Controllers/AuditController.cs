using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin/audit")]
public sealed class AuditController(RentalDbContext db) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAudit(int take, CancellationToken ct) =>
        Ok(await db.AuditLogs.AsNoTracking().OrderByDescending(x => x.ChangedAt)
            .Take(Math.Clamp(take == 0 ? 100 : take, 1, 500)).ToListAsync(ct));
}
