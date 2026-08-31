using Microsoft.AspNetCore.Mvc;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin/evidence")]
public sealed class EvidenceController(IFileStorage storage, RentalDbContext db) : AdminControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveSlipAsync(stream, file.ContentType, DateTime.UtcNow, ct);
        try
        {
            db.AuditLogs.Add(Audit("Evidence", stored.Sha256[..12], "Upload", null, stored.RelativePath));
            await db.SaveChangesAsync(ct);
            return Ok(new { path = stored.RelativePath, stored.Sha256 });
        }
        catch
        {
            await storage.DeleteAsync(stored.RelativePath, ct);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> Read(string path, CancellationToken ct) =>
        File(await storage.OpenReadAsync(path, ct), "image/jpeg");
}
