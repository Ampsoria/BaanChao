using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin/rooms")]
public sealed class RoomsController(RentalDbContext db) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRooms(CancellationToken ct) =>
        Ok(await db.Rooms.AsNoTracking().OrderBy(x => x.RoomNumber).ToListAsync(ct));

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

    [HttpGet("status")]
    public async Task<IActionResult> GetRoomStatus(CancellationToken ct)
    {
        var data = await db.Rooms.AsNoTracking().OrderBy(x => x.RoomNumber).Select(room => new
        {
            room.RoomId,
            room.RoomNumber,
            room.MonthlyRent,
            room.PayeeCents,
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
