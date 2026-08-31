using Microsoft.AspNetCore.Mvc;
using RentalManager.Api.Models;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using RentalManager.Infrastructure.Data;
using RentalManager.Core.Entities;
using System.Security.Cryptography;
using System.Text;

namespace RentalManager.Api.Controllers;

[Route("api/admin/tenants")]
public sealed class TenantsController(RentalOperationsService service, RentalDbContext db) : AdminControllerBase
{
    [HttpPost("preview-move-in")]
    public async Task<IActionResult> PreviewMoveIn(MoveInPreviewRequest request, CancellationToken ct) =>
        Ok(await service.PreviewMoveInAsync(request.RoomId, request.MovedInAt, ct));

    [HttpPost("move-in")]
    public async Task<IActionResult> MoveIn(MoveInCommand command, CancellationToken ct) =>
        Ok(await service.MoveInAsync(command, UserName, ct));

    [HttpPost("preview-move-out")]
    public async Task<IActionResult> PreviewMoveOut(MoveOutCommand command, CancellationToken ct) =>
        Ok(await service.PreviewMoveOutAsync(command, ct));

    [HttpPost("{id:int}/move-out")]
    public async Task<IActionResult> MoveOut(int id, MoveOutRequest request, CancellationToken ct)
    {
        var command = new MoveOutCommand(
            id, request.MoveOutDate, request.WaterFinal, request.ElectricFinal, request.Deductions ?? []);
        return Ok(await service.MoveOutAsync(command, UserName, ct));
    }

    [HttpPatch("{id:int}/channel")]
    public async Task<IActionResult> UpdateChannel(int id, UpdateChannelRequest request, CancellationToken ct)
    {
        if (!TenantChannels.IsValid(request.PreferredChannel))
            return BadRequest(new { message = "ช่องทางรับบิลต้องเป็น Line หรือ Paper" });
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.TenantId == id && x.MovedOutAt == null, ct);
        if (tenant is null) return NotFound();
        if (request.PreferredChannel == TenantChannels.Line && string.IsNullOrWhiteSpace(tenant.LineUserId))
            return BadRequest(new { message = "ผู้เช่ายังไม่ได้ผูก LINE จึงตั้งเป็นช่องทาง Line ไม่ได้" });
        var old = tenant.PreferredChannel;
        tenant.PreferredChannel = request.PreferredChannel;
        db.AuditLogs.Add(Audit("Tenant", id.ToString(), "PreferredChannel", old, tenant.PreferredChannel));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = $"ตั้งช่องทางรับบิลเป็น {tenant.PreferredChannel} แล้ว", tenant.PreferredChannel });
    }

    [HttpPost("{id:int}/line-link-code")]
    public async Task<IActionResult> CreateLineLinkCode(int id, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(x => x.TenantId == id && x.MovedOutAt == null, ct)) return NotFound();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
        db.TenantLinkCodes.Add(new TenantLinkCode
        {
            TenantId = id,
            CodeHash = hash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        db.AuditLogs.Add(Audit("Tenant", id.ToString(), "CreateLineLinkCode", null, "ExpiresIn15Minutes"));
        await db.SaveChangesAsync(ct);
        return Ok(new { code, expiresAt = DateTime.UtcNow.AddMinutes(15), instruction = $"ส่งข้อความ: ผูกห้อง {code}" });
    }
}
