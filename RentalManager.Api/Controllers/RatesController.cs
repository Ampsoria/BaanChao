using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin")]
public sealed class RatesController(RentalDbContext db) : AdminControllerBase
{
    [HttpGet("rates")]
    public async Task<IActionResult> GetRates(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var rates = await db.UtilityRates.AsNoTracking().OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);
        var currentId = rates.FirstOrDefault(x => x.EffectiveFrom <= today)?.RateId;
        return Ok(new { currentId, rates });
    }

    [HttpPost("rates")]
    public async Task<IActionResult> CreateRate(CreateRateRequest request, CancellationToken ct)
    {
        if (request.Water < 0 || request.Electric < 0 || request.Trash < 0)
            return BadRequest(new { message = "ราคาต้องไม่ติดลบ" });
        if (request.Note?.Length > 200)
            return BadRequest(new { message = "หมายเหตุต้องยาวไม่เกิน 200 ตัวอักษร" });
        var latest = await db.UtilityRates.OrderByDescending(x => x.EffectiveFrom).FirstAsync(ct);
        if (request.EffectiveFrom <= latest.EffectiveFrom)
            return BadRequest(new { message = $"วันที่มีผลต้องหลัง {latest.EffectiveFrom:yyyy-MM-dd}" });

        var rate = new UtilityRate
        {
            EffectiveFrom = request.EffectiveFrom,
            WaterPerUnit = request.Water,
            ElectricPerUnit = request.Electric,
            TrashPerMonth = request.Trash,
            Note = request.Note?.Trim()
        };
        db.UtilityRates.Add(rate);
        db.AuditLogs.Add(Audit("UtilityRate", request.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Create", null, $"{request.Water}/{request.Electric}/{request.Trash}"));
        await db.SaveChangesAsync(ct);
        return Created($"/api/admin/rates/{rate.RateId}", new
        {
            rate,
            warning = request.Water == 0 || request.Electric == 0 || request.Trash == 0
                ? "มีราคาบางรายการเป็น 0 กรุณาตรวจสอบ"
                : null
        });
    }

    [HttpGet("billing-policy")]
    public async Task<IActionResult> GetBillingPolicies(CancellationToken ct) =>
        Ok(await db.BillingPolicies.AsNoTracking().OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct));

    [HttpPost("billing-policy")]
    public async Task<IActionResult> CreateBillingPolicy(CreatePolicyRequest request, CancellationToken ct)
    {
        if (request.GraceDays is < 1 or > 28 || request.LateFeeAmount < 0 || request.LateFeeCap < 0)
            return BadRequest(new { message = "วันผ่อนผันต้องอยู่ระหว่าง 1–28 และค่าปรับต้องไม่ติดลบ" });
        if (request.Note?.Length > 200)
            return BadRequest(new { message = "หมายเหตุต้องยาวไม่เกิน 200 ตัวอักษร" });
        if (!Enum.TryParse<LateFeeType>(request.LateFeeType, true, out var lateFeeType))
            return BadRequest(new { message = "lateFeeType ต้องเป็น None, PerDay หรือ Flat" });
        var latest = await db.BillingPolicies.OrderByDescending(x => x.EffectiveFrom).FirstAsync(ct);
        if (request.EffectiveFrom <= latest.EffectiveFrom)
            return BadRequest(new { message = $"วันที่มีผลต้องหลัง {latest.EffectiveFrom:yyyy-MM-dd}" });

        var policy = new BillingPolicy
        {
            EffectiveFrom = request.EffectiveFrom,
            GraceDays = request.GraceDays,
            LateFeeType = lateFeeType,
            LateFeeAmount = request.LateFeeAmount,
            LateFeeCap = request.LateFeeCap,
            Note = request.Note?.Trim()
        };
        db.BillingPolicies.Add(policy);
        db.AuditLogs.Add(Audit("BillingPolicy", request.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "Create", null, request.LateFeeType));
        await db.SaveChangesAsync(ct);
        return Created("/api/admin/billing-policy", policy);
    }

    [HttpPost("preview-invoice")]
    public async Task<IActionResult> PreviewInvoice(PreviewInvoiceRequest request, CancellationToken ct)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.RoomId == request.RoomId, ct);
        if (room is null) return NotFound();
        if (!DateOnly.TryParseExact(request.BillingPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month))
            return BadRequest(new { message = "งวดบิลต้องอยู่ในรูปแบบ YYYY-MM" });
        var quote = BillingCalculator.CalculateInvoice(room.MonthlyRent, request.MovedInAt ?? month,
            month.Year, month.Month, request.WaterPrevious, request.WaterCurrent,
            request.ElectricPrevious, request.ElectricCurrent, request.WaterRate,
            request.ElectricRate, request.Trash);
        return Ok(quote);
    }
}
