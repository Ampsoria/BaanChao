using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Controllers;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;
using Xunit;

namespace RentalManager.Tests;

public sealed class RoomManagementTests
{
    [Fact]
    public async Task CreateRoom_AssignsTheNextAvailablePayeeCents()
    {
        await using var db = CreateDatabase();
        var controller = CreateController(db);

        var result = await controller.CreateRoom(
            new CreateRoomRequest("7", 2_400m), TestContext.Current.CancellationToken);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var room = Assert.IsType<Room>(created.Value);
        Assert.Equal("7", room.RoomNumber);
        Assert.Equal(.07m, room.PayeeCents);
        Assert.True(room.IsActive);
        Assert.Equal(7, await db.Rooms.CountAsync(TestContext.Current.CancellationToken));
        Assert.Contains(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken),
            entry => entry.EntityName == "Room" && entry.EntityKey == "7" && entry.FieldName == "Create");
    }

    [Fact]
    public async Task DeactivateAndRestoreRoom_PreserveTheRoomRecord()
    {
        await using var db = CreateDatabase();
        var controller = CreateController(db);
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<OkObjectResult>(await controller.DeactivateRoom("1", ct));
        Assert.False((await db.Rooms.SingleAsync(room => room.RoomNumber == "1", ct)).IsActive);

        Assert.IsType<OkObjectResult>(await controller.RestoreRoom("1", ct));
        Assert.True((await db.Rooms.SingleAsync(room => room.RoomNumber == "1", ct)).IsActive);
        Assert.Equal(6, await db.Rooms.CountAsync(ct));
    }

    [Fact]
    public async Task DeactivateRoom_RejectsARoomWithAnActiveTenant()
    {
        await using var db = CreateDatabase();
        db.Tenants.Add(new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่าทดสอบ",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 1_800m,
            MinimumStayMonths = 5
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = CreateController(db);

        var result = await controller.DeactivateRoom("1", TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.True((await db.Rooms.SingleAsync(
            room => room.RoomNumber == "1", TestContext.Current.CancellationToken)).IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("room/7")]
    [InlineData("12345678901")]
    public async Task CreateRoom_RejectsInvalidRoomNumbers(string roomNumber)
    {
        await using var db = CreateDatabase();
        var result = await CreateController(db).CreateRoom(
            new CreateRoomRequest(roomNumber, 2_000m), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(6, await db.Rooms.CountAsync(TestContext.Current.CancellationToken));
    }

    private static RentalDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"room-management-{Guid.NewGuid():N}")
            .Options;
        var db = new RentalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RoomsController CreateController(RentalDbContext db)
    {
        var controller = new RoomsController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "RoomTest")], "Test"))
            }
        };
        return controller;
    }
}
