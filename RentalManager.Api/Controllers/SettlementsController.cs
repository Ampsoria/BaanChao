using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin/settlements")]
public sealed class SettlementsController(
    RentalDbContext db, IReceiptService receipts, IFileStorage storage) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await db.MoveOutSettlements.AsNoTracking()
        .OrderByDescending(x => x.MoveOutDate).Select(x => new
        {
            x.SettlementId,
            x.Tenant.Room.RoomNumber,
            x.Tenant.FullName,
            x.MoveOutDate,
            x.IsForfeited,
            x.TotalDeducted,
            x.RefundAmount,
            x.AmountDueFromTenant,
            x.RefundedAt
        }).ToListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var settlement = await db.MoveOutSettlements.AsNoTracking().Include(x => x.Deductions)
            .Where(x => x.SettlementId == id).Select(x => new
            {
                x.SettlementId,
                x.TenantId,
                x.Tenant.Room.RoomNumber,
                x.Tenant.FullName,
                x.MoveOutDate,
                x.DepositAmount,
                x.FinalWaterAmount,
                x.FinalElectricAmount,
                x.OutstandingAmount,
                x.DeductionAmount,
                x.IsForfeited,
                x.ForfeitReason,
                x.MonthsStayed,
                x.TotalDeducted,
                x.RefundAmount,
                x.AmountDueFromTenant,
                x.ForfeitedAmount,
                x.RefundedAt,
                x.Deductions
            }).SingleOrDefaultAsync(ct);
        return settlement is null ? NotFound() : Ok(settlement);
    }

    [HttpGet("{id:int}/statement.pdf")]
    public async Task<IActionResult> Statement(int id, CancellationToken ct)
    {
        var entity = await db.MoveOutSettlements.AsNoTracking().Include(x => x.Deductions)
            .Include(x => x.Tenant).ThenInclude(x => x.Room).SingleOrDefaultAsync(x => x.SettlementId == id, ct);
        if (entity is null) return NotFound();
        var deductions = new List<SettlementStatementDeduction>();
        foreach (var deduction in entity.Deductions)
        {
            byte[]? photo = null;
            if (!string.IsNullOrWhiteSpace(deduction.PhotoUrl))
            {
                await using var stream = await storage.OpenReadAsync(deduction.PhotoUrl, ct);
                await using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, ct);
                photo = memory.ToArray();
            }
            deductions.Add(new SettlementStatementDeduction(deduction.Description, deduction.Amount, photo));
        }
        var settlement = new SettlementStatementData(
            entity.SettlementId, entity.Tenant.Room.RoomNumber, entity.Tenant.FullName, entity.MoveOutDate,
            entity.DepositAmount, entity.FinalWaterAmount, entity.FinalElectricAmount, entity.OutstandingAmount,
            deductions, entity.IsForfeited, entity.ForfeitedAmount, entity.RefundAmount, entity.AmountDueFromTenant);
        return File(receipts.CreateSettlementStatement(settlement), "application/pdf", $"settlement-{id}.pdf");
    }

    [HttpPost("{id:int}/mark-refunded")]
    public async Task<IActionResult> MarkRefunded(int id, [FromBody] RefundRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Method) || request.Method.Length > 20)
            return BadRequest(new { message = "วิธีคืนเงินต้องมีความยาวไม่เกิน 20 ตัวอักษร" });
        var settlement = await db.MoveOutSettlements.Include(x => x.Tenant)
            .SingleOrDefaultAsync(x => x.SettlementId == id, ct);
        if (settlement is null) return NotFound();
        if (settlement.RefundAmount <= 0) return BadRequest(new { message = "รายการนี้ไม่มียอดที่ต้องคืน" });
        if (settlement.RefundedAt is not null) return Conflict(new { message = "บันทึกคืนมัดจำแล้ว" });
        settlement.RefundedAt = DateTime.UtcNow;
        settlement.RefundMethod = request.Method;
        settlement.Tenant.DepositRefundedAt = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Bangkok"));
        db.AuditLogs.Add(Audit("MoveOutSettlement", id.ToString(), "Refund", null,
            $"{settlement.RefundAmount:0.00}/{request.Method}"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "บันทึกคืนมัดจำแล้ว" });
    }
}

public sealed record RefundRequest(string Method);
