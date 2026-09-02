using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
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

    [HttpGet("checkpoints")]
    public async Task<IActionResult> GetCheckpoints(int? roomId, CancellationToken ct)
    {
        var query = db.MeterCheckpoints.AsNoTracking().AsQueryable();
        if (roomId.HasValue) query = query.Where(x => x.RoomId == roomId.Value);
        return Ok(await query.OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.MeterCheckpointId).Select(x => new
            {
                x.MeterCheckpointId,
                x.RoomId,
                x.Room.RoomNumber,
                x.TenantId,
                x.Tenant.FullName,
                x.RecordedAt,
                x.Kind,
                x.WaterReading,
                x.ElectricReading
            }).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddMeter(MeterReadingCommand command, CancellationToken ct) =>
        Ok(await service.AddMeterReadingAsync(command, UserName, ct));

    [HttpPost("history")]
    public async Task<IActionResult> AddHistoricalMeter(CreateHistoricalMeterRequest request, CancellationToken ct)
    {
        if (!TryValidateReading(request.BillingPeriod, request.ReadAt,
                request.WaterPrevious, request.WaterCurrent,
                request.ElectricPrevious, request.ElectricCurrent, out var validationMessage))
            return BadRequest(new { message = validationMessage });

        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.RoomId == request.RoomId, ct);
        if (room is null) return NotFound(new { message = "ไม่พบห้องพัก" });

        var readings = await db.MeterReadings.Where(x => x.RoomId == request.RoomId).ToListAsync(ct);
        if (readings.Any(x => x.BillingPeriod == request.BillingPeriod))
            return Conflict(new { message = $"ห้อง {room.RoomNumber} มีข้อมูลมิเตอร์งวด {request.BillingPeriod} แล้ว กรุณากดแก้ไขรายการเดิม" });

        var previous = readings.Where(x => string.CompareOrdinal(x.BillingPeriod, request.BillingPeriod) < 0)
            .OrderByDescending(x => x.BillingPeriod).FirstOrDefault();
        var next = readings.Where(x => string.CompareOrdinal(x.BillingPeriod, request.BillingPeriod) > 0)
            .OrderBy(x => x.BillingPeriod).FirstOrDefault();

        if (previous is not null &&
            (request.WaterPrevious != previous.WaterCurrent || request.ElectricPrevious != previous.ElectricCurrent))
            return Conflict(new
            {
                message = $"เลขครั้งก่อนต้องต่อจากงวด {previous.BillingPeriod}: น้ำ {previous.WaterCurrent:N2} ไฟ {previous.ElectricCurrent:N2}"
            });
        if (next is not null &&
            (request.WaterCurrent > next.WaterCurrent || request.ElectricCurrent > next.ElectricCurrent))
            return Conflict(new
            {
                message = $"เลขครั้งใหม่ต้องไม่เกินงวดถัดไป {next.BillingPeriod}: น้ำ {next.WaterCurrent:N2} ไฟ {next.ElectricCurrent:N2}"
            });

        var affectedPeriods = new[] { request.BillingPeriod }
            .Concat(next is null ? [] : [next.BillingPeriod]);
        var lockedPeriod = await FindLockedPeriodAsync(request.RoomId, affectedPeriods, ct);
        if (lockedPeriod is not null)
            return Conflict(new { message = $"แก้ข้อมูลไม่ได้ เพราะงวดมิเตอร์ {lockedPeriod} ถูกนำไปออกบิลแล้ว ต้อง void บิลที่เกี่ยวข้องก่อน" });

        var reading = new MeterReading
        {
            RoomId = request.RoomId,
            BillingPeriod = request.BillingPeriod,
            ReadAt = request.ReadAt,
            WaterPrev = request.WaterPrevious,
            WaterCurrent = request.WaterCurrent,
            ElectricPrev = request.ElectricPrevious,
            ElectricCurrent = request.ElectricCurrent
        };
        db.MeterReadings.Add(reading);
        if (next is not null)
        {
            next.WaterPrev = request.WaterCurrent;
            next.ElectricPrev = request.ElectricCurrent;
        }
        db.AuditLogs.Add(Audit("MeterReading", $"{room.RoomNumber}:{request.BillingPeriod}", "HistoryCreate", null,
            $"Water={request.WaterPrevious}->{request.WaterCurrent}, Electric={request.ElectricPrevious}->{request.ElectricCurrent}"));
        await db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, new { reading.ReadingId, message = "บันทึกข้อมูลมิเตอร์ย้อนหลังแล้ว" });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateMeter(int id, UpdateMeterRequest request, CancellationToken ct)
    {
        var reading = await db.MeterReadings.SingleOrDefaultAsync(x => x.ReadingId == id, ct);
        if (reading is null) return NotFound();
        if (!TryValidateReading(reading.BillingPeriod, request.ReadAt,
                request.WaterPrevious, request.WaterCurrent,
                request.ElectricPrevious, request.ElectricCurrent, out var validationMessage))
            return BadRequest(new { message = validationMessage });

        var roomReadings = await db.MeterReadings.Where(x => x.RoomId == reading.RoomId && x.ReadingId != id)
            .ToListAsync(ct);
        var previous = roomReadings.Where(x => string.CompareOrdinal(x.BillingPeriod, reading.BillingPeriod) < 0)
            .OrderByDescending(x => x.BillingPeriod).FirstOrDefault();
        var next = roomReadings.Where(x => string.CompareOrdinal(x.BillingPeriod, reading.BillingPeriod) > 0)
            .OrderBy(x => x.BillingPeriod).FirstOrDefault();

        if (previous is not null &&
            (request.WaterPrevious != previous.WaterCurrent || request.ElectricPrevious != previous.ElectricCurrent))
            return Conflict(new
            {
                message = $"เลขครั้งก่อนต้องเท่ากับงวด {previous.BillingPeriod}: น้ำ {previous.WaterCurrent:N2} ไฟ {previous.ElectricCurrent:N2}"
            });
        if (next is not null &&
            (request.WaterCurrent > next.WaterCurrent || request.ElectricCurrent > next.ElectricCurrent))
            return Conflict(new
            {
                message = $"เลขที่แก้ต้องไม่เกินงวดถัดไป {next.BillingPeriod}: น้ำ {next.WaterCurrent:N2} ไฟ {next.ElectricCurrent:N2}"
            });

        var affectedPeriods = new[] { reading.BillingPeriod }
            .Concat(next is null ? [] : [next.BillingPeriod]);
        var lockedPeriod = await FindLockedPeriodAsync(reading.RoomId, affectedPeriods, ct);
        if (lockedPeriod is not null)
            return Conflict(new { message = $"แก้ข้อมูลไม่ได้ เพราะงวดมิเตอร์ {lockedPeriod} ถูกนำไปออกบิลแล้ว ต้อง void บิลที่เกี่ยวข้องก่อน" });

        var old = $"Water={reading.WaterPrev}->{reading.WaterCurrent}, Electric={reading.ElectricPrev}->{reading.ElectricCurrent}";
        reading.ReadAt = request.ReadAt;
        reading.WaterPrev = request.WaterPrevious;
        reading.WaterCurrent = request.WaterCurrent;
        reading.ElectricPrev = request.ElectricPrevious;
        reading.ElectricCurrent = request.ElectricCurrent;
        if (next is not null)
        {
            next.WaterPrev = request.WaterCurrent;
            next.ElectricPrev = request.ElectricCurrent;
        }
        db.AuditLogs.Add(Audit("MeterReading", id.ToString(CultureInfo.InvariantCulture), "Update", old,
            $"Water={reading.WaterPrev}->{reading.WaterCurrent}, Electric={reading.ElectricPrev}->{reading.ElectricCurrent}"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = next is null ? "แก้ไขเลขมิเตอร์แล้ว" : $"แก้ไขแล้ว และปรับเลขครั้งก่อนของงวด {next.BillingPeriod} ให้ต่อกันแล้ว" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMeter(int id, CancellationToken ct)
    {
        var reading = await db.MeterReadings.SingleOrDefaultAsync(x => x.ReadingId == id, ct);
        if (reading is null) return NotFound();

        var roomReadings = await db.MeterReadings.Where(x => x.RoomId == reading.RoomId && x.ReadingId != id)
            .ToListAsync(ct);
        var previous = roomReadings.Where(x => string.CompareOrdinal(x.BillingPeriod, reading.BillingPeriod) < 0)
            .OrderByDescending(x => x.BillingPeriod).FirstOrDefault();
        var next = roomReadings.Where(x => string.CompareOrdinal(x.BillingPeriod, reading.BillingPeriod) > 0)
            .OrderBy(x => x.BillingPeriod).FirstOrDefault();
        var affectedPeriods = new[] { reading.BillingPeriod }
            .Concat(next is null ? [] : [next.BillingPeriod]);
        var lockedPeriod = await FindLockedPeriodAsync(reading.RoomId, affectedPeriods, ct);
        if (lockedPeriod is not null)
            return Conflict(new { message = $"ลบไม่ได้ เพราะงวดมิเตอร์ {lockedPeriod} ถูกนำไปออกบิลแล้ว ต้อง void บิลที่เกี่ยวข้องก่อน" });

        if (previous is not null && next is not null)
        {
            if (next.WaterCurrent < previous.WaterCurrent || next.ElectricCurrent < previous.ElectricCurrent)
                return Conflict(new { message = "ลบไม่ได้ เพราะจะทำให้เลขงวดถัดไปน้อยกว่าเลขครั้งก่อน" });
            next.WaterPrev = previous.WaterCurrent;
            next.ElectricPrev = previous.ElectricCurrent;
        }
        db.MeterReadings.Remove(reading);
        db.AuditLogs.Add(Audit("MeterReading", id.ToString(CultureInfo.InvariantCulture), "Delete",
            $"{reading.BillingPeriod}: Water={reading.WaterCurrent}, Electric={reading.ElectricCurrent}", null));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string?> FindLockedPeriodAsync(int roomId, IEnumerable<string> periods, CancellationToken ct)
    {
        var periodList = periods.Distinct().ToList();
        var billingPeriods = periodList.Select(NextPeriod).ToList();
        return await db.Invoices.AsNoTracking()
            .Where(x => x.RoomId == roomId && x.Status != InvoiceStatus.Void &&
                        ((x.UtilityPeriod != null && periodList.Contains(x.UtilityPeriod)) || billingPeriods.Contains(x.BillingPeriod)))
            .Select(x => x.UtilityPeriod ?? periodList.First())
            .FirstOrDefaultAsync(ct);
    }

    private static bool TryValidateReading(
        string billingPeriod, DateOnly readAt,
        decimal waterPrevious, decimal waterCurrent,
        decimal electricPrevious, decimal electricCurrent,
        out string message)
    {
        if (!DateOnly.TryParseExact(billingPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var periodStart))
        {
            message = "งวดมิเตอร์ต้องอยู่ในรูปแบบ YYYY-MM";
            return false;
        }
        if (readAt.Year != periodStart.Year || readAt.Month != periodStart.Month)
        {
            message = "วันที่จดต้องอยู่ในเดือนเดียวกับงวดมิเตอร์";
            return false;
        }
        if (waterPrevious < 0 || electricPrevious < 0 || waterCurrent < waterPrevious || electricCurrent < electricPrevious)
        {
            message = "เลขมิเตอร์ต้องไม่ติดลบ และเลขครั้งใหม่ต้องไม่น้อยกว่าเลขครั้งก่อน";
            return false;
        }
        message = "";
        return true;
    }

    private static string NextPeriod(string billingPeriod)
    {
        var start = DateOnly.ParseExact(billingPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return start.AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }
}
