using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin/rooms")]
public sealed class RoomsController(RentalDbContext db) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRooms(CancellationToken ct) =>
        Ok(await db.Rooms.AsNoTracking().OrderBy(x => x.RoomNumber).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> CreateRoom(CreateRoomRequest request, CancellationToken ct)
    {
        var roomNumber = request.RoomNumber?.Trim() ?? "";
        if (roomNumber.Length is < 1 or > 10)
            return BadRequest(new { message = "เลขห้องต้องมี 1–10 ตัวอักษร" });
        if (roomNumber.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            return BadRequest(new { message = "เลขห้องใช้ได้เฉพาะตัวอักษร ตัวเลข - และ _" });
        if (request.MonthlyRent < 0 || decimal.Round(request.MonthlyRent, 2) != request.MonthlyRent)
            return BadRequest(new { message = "ค่าเช่าต้องไม่ติดลบและมีทศนิยมไม่เกิน 2 ตำแหน่ง" });
        if (await db.Rooms.AnyAsync(x => x.RoomNumber == roomNumber, ct))
            return Conflict(new { message = $"มีห้อง {roomNumber} อยู่แล้ว หากปิดใช้งานไว้ให้กดเปิดใช้งานคืน" });

        var usedCents = (await db.Rooms.AsNoTracking().Select(x => x.PayeeCents).ToListAsync(ct)).ToHashSet();
        var payeeCents = Enumerable.Range(1, 99).Select(value => value / 100m)
            .FirstOrDefault(value => !usedCents.Contains(value));
        if (payeeCents == 0)
            return Conflict(new { message = "ไม่มีเศษสตางค์ว่างสำหรับระบุห้อง ระบบรองรับได้สูงสุด 99 ห้อง" });

        var room = new Room
        {
            RoomNumber = roomNumber,
            MonthlyRent = request.MonthlyRent,
            PayeeCents = payeeCents,
            IsActive = true
        };
        db.Rooms.Add(room);
        db.AuditLogs.Add(Audit("Room", roomNumber, "Create", null,
            $"Rent={request.MonthlyRent.ToString(CultureInfo.InvariantCulture)}, PayeeCents={payeeCents.ToString(CultureInfo.InvariantCulture)}"));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "เลขห้องหรือเศษสตางค์นี้ถูกใช้พร้อมกัน กรุณาลองใหม่" });
        }
        return StatusCode(StatusCodes.Status201Created, room);
    }

    [HttpPatch("{roomNumber}")]
    public async Task<IActionResult> UpdateRoomRent(string roomNumber, UpdateRentRequest request, CancellationToken ct)
    {
        if (request.MonthlyRent < 0)
            return BadRequest(new { message = "ค่าเช่าต้องไม่ติดลบ" });
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.RoomNumber == roomNumber, ct);
        if (room is null) return NotFound();
        var old = room.MonthlyRent;
        room.MonthlyRent = request.MonthlyRent;
        db.AuditLogs.Add(Audit("Room", room.RoomNumber, "MonthlyRent",
            old.ToString(CultureInfo.InvariantCulture), room.MonthlyRent.ToString(CultureInfo.InvariantCulture)));
        await db.SaveChangesAsync(ct);
        return Ok(room);
    }

    [HttpDelete("{roomNumber}")]
    public async Task<IActionResult> DeactivateRoom(string roomNumber, CancellationToken ct)
    {
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.RoomNumber == roomNumber, ct);
        if (room is null) return NotFound();
        if (!room.IsActive) return Ok(new { message = $"ห้อง {room.RoomNumber} ปิดใช้งานอยู่แล้ว" });
        if (await db.Tenants.AnyAsync(x => x.RoomId == room.RoomId && x.MovedOutAt == null, ct))
            return Conflict(new { message = $"ปิดห้อง {room.RoomNumber} ไม่ได้ เพราะยังมีผู้เช่าอยู่ กรุณาทำรายการย้ายออกก่อน" });

        room.IsActive = false;
        db.AuditLogs.Add(Audit("Room", room.RoomNumber, "IsActive", "True", "False"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = $"ปิดใช้งานห้อง {room.RoomNumber} แล้ว ประวัติเดิมยังอยู่ครบ" });
    }

    [HttpPost("{roomNumber}/restore")]
    public async Task<IActionResult> RestoreRoom(string roomNumber, CancellationToken ct)
    {
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.RoomNumber == roomNumber, ct);
        if (room is null) return NotFound();
        if (room.IsActive) return Ok(new { message = $"ห้อง {room.RoomNumber} เปิดใช้งานอยู่แล้ว" });

        room.IsActive = true;
        db.AuditLogs.Add(Audit("Room", room.RoomNumber, "IsActive", "False", "True"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = $"เปิดใช้งานห้อง {room.RoomNumber} แล้ว" });
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetRoomStatus(CancellationToken ct)
    {
        var data = await db.Rooms.AsNoTracking().OrderBy(x => x.RoomNumber).Select(room => new
        {
            room.RoomId,
            room.RoomNumber,
            room.MonthlyRent,
            room.PayeeCents,
            room.IsActive,
            Tenant = room.Tenants.Where(x => x.MovedOutAt == null).Select(x => new
            {
                x.TenantId,
                x.FullName,
                x.Phone,
                x.LineUserId,
                x.MovedInAt,
                x.DepositAmount,
                x.MinimumStayMonths,
                x.PreferredChannel
            }).FirstOrDefault(),
            LatestMeter = room.MeterReadings.OrderByDescending(x => x.ReadAt).Select(x => new
            {
                x.ReadAt,
                x.WaterCurrent,
                x.ElectricCurrent,
                x.BillingPeriod
            }).FirstOrDefault()
        }).ToListAsync(ct);
        return Ok(data);
    }
}
