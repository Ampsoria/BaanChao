using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;

namespace RentalManager.Api.Controllers;

[Route("api/admin/meters")]
public sealed class MetersController(RentalDbContext db, RentalOperationsService service) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMeters(int? roomId, CancellationToken ct)
    {
        var query = db.MeterReadings.AsNoTracking().Include(x => x.Room).AsQueryable();
        if (roomId.HasValue) query = query.Where(x => x.RoomId == roomId.Value);
        return Ok(await query.OrderByDescending(x => x.ReadAt).Select(x => new
        {
            x.ReadingId,
            x.RoomId,
            x.Room.RoomNumber,
            x.BillingPeriod,
            x.ReadAt,
            x.WaterPrev,
            x.WaterCurrent,
            x.WaterUnits,
            x.ElectricPrev,
            x.ElectricCurrent,
            x.ElectricUnits
        }).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddMeter(MeterReadingCommand command, CancellationToken ct) =>
        Ok(await service.AddMeterReadingAsync(command, UserName, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateMeter(int id, UpdateMeterRequest request, CancellationToken ct)
    {
        var reading = await db.MeterReadings.SingleOrDefaultAsync(x => x.ReadingId == id, ct);
        if (reading is null) return NotFound();
        if (request.WaterCurrent < reading.WaterPrev || request.ElectricCurrent < reading.ElectricPrev)
            return BadRequest(new { message = "เลขมิเตอร์ที่แก้ต้องไม่น้อยกว่าเลขครั้งก่อน" });
        var old = $"Water={reading.WaterCurrent}, Electric={reading.ElectricCurrent}";
        reading.ReadAt = request.ReadAt;
        reading.WaterCurrent = request.WaterCurrent;
        reading.ElectricCurrent = request.ElectricCurrent;
        db.AuditLogs.Add(Audit("MeterReading", id.ToString(CultureInfo.InvariantCulture), "Update", old,
            $"Water={reading.WaterCurrent}, Electric={reading.ElectricCurrent}"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "แก้ไขเลขมิเตอร์แล้ว" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMeter(int id, CancellationToken ct)
    {
        var reading = await db.MeterReadings.SingleOrDefaultAsync(x => x.ReadingId == id, ct);
        if (reading is null) return NotFound();
        var hasInvoice = await db.Invoices.AnyAsync(
            x => x.RoomId == reading.RoomId && x.BillingPeriod == reading.BillingPeriod, ct);
        if (hasInvoice)
            return BadRequest(new { message = "ลบไม่ได้เพราะงวดนี้ออกบิลแล้ว ให้ void บิลก่อน" });
        db.MeterReadings.Remove(reading);
        db.AuditLogs.Add(Audit("MeterReading", id.ToString(CultureInfo.InvariantCulture), "Delete",
            $"{reading.BillingPeriod}: Water={reading.WaterCurrent}, Electric={reading.ElectricCurrent}", null));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
