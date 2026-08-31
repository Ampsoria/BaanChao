using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentalManager.Api.Controllers;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;
using Xunit;

namespace RentalManager.Tests;

public sealed class MeterHistoryTests
{
    [Fact]
    public async Task AddHistoricalMeter_SavesPreviousCurrentAndAudit()
    {
        await using var db = CreateDatabase();
        var controller = CreateController(db);
        var ct = TestContext.Current.CancellationToken;

        var result = await controller.AddHistoricalMeter(new CreateHistoricalMeterRequest(
            1, "2026-08", new DateOnly(2026, 8, 31),
            100m, 105m, 2_503m, 2_535m), ct);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var reading = await db.MeterReadings.SingleAsync(ct);
        Assert.Equal(100m, reading.WaterPrev);
        Assert.Equal(105m, reading.WaterCurrent);
        Assert.Equal(2_503m, reading.ElectricPrev);
        Assert.Equal(2_535m, reading.ElectricCurrent);
        Assert.Contains(await db.AuditLogs.ToListAsync(ct),
            entry => entry.EntityName == "MeterReading" && entry.FieldName == "HistoryCreate");
    }

    [Fact]
    public async Task AddHistoricalMeter_RejectsAChainThatDoesNotMatchThePreviousMonth()
    {
        await using var db = CreateDatabase();
        db.MeterReadings.Add(Reading("2026-07", 10m, 20m, 100m, 120m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).AddHistoricalMeter(new CreateHistoricalMeterRequest(
            1, "2026-08", new DateOnly(2026, 8, 31),
            19m, 25m, 119m, 130m), TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(1, await db.MeterReadings.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateMeter_UpdatesTheNextMonthsPreviousReading()
    {
        await using var db = CreateDatabase();
        var july = Reading("2026-07", 10m, 20m, 100m, 120m);
        var august = Reading("2026-08", 20m, 30m, 120m, 150m);
        db.MeterReadings.AddRange(july, august);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).UpdateMeter(july.ReadingId,
            new UpdateMeterRequest(new DateOnly(2026, 7, 31), 10m, 22m, 100m, 125m),
            TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(22m, august.WaterPrev);
        Assert.Equal(125m, august.ElectricPrev);
    }

    [Fact]
    public async Task UpdateMeter_RejectsAReadingAlreadyUsedByAnInvoice()
    {
        await using var db = CreateDatabase();
        var reading = Reading("2026-08", 10m, 20m, 100m, 120m);
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่าทดสอบ",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 1_800m,
            MinimumStayMonths = 5
        };
        db.AddRange(reading, tenant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Invoices.Add(new Invoice
        {
            RoomId = 1,
            TenantId = tenant.TenantId,
            BillingPeriod = "2026-09",
            UtilityPeriod = "2026-08",
            DueDate = new DateOnly(2026, 9, 5),
            PeriodStart = new DateOnly(2026, 9, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            DaysCharged = 30,
            DaysInPeriod = 30,
            RentAmount = 1_800m,
            WaterUnits = 10m,
            WaterRate = 20m,
            ElectricUnits = 20m,
            ElectricRate = 12m,
            TrashAmount = 40m
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).UpdateMeter(reading.ReadingId,
            new UpdateMeterRequest(new DateOnly(2026, 8, 31), 10m, 21m, 100m, 121m),
            TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(20m, reading.WaterCurrent);
        Assert.Equal(120m, reading.ElectricCurrent);
    }

    private static MeterReading Reading(
        string period, decimal waterPrevious, decimal waterCurrent,
        decimal electricPrevious, decimal electricCurrent) => new()
        {
            RoomId = 1,
            BillingPeriod = period,
            ReadAt = DateOnly.Parse(period + "-28"),
            WaterPrev = waterPrevious,
            WaterCurrent = waterCurrent,
            ElectricPrev = electricPrevious,
            ElectricCurrent = electricCurrent
        };

    private static RentalDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"meter-history-{Guid.NewGuid():N}")
            .Options;
        var db = new RentalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static MetersController CreateController(RentalDbContext db)
    {
        var controller = new MetersController(db,
            new RentalOperationsService(db, Options.Create(new BillingOptions())));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "MeterHistoryTest")], "Test"))
            }
        };
        return controller;
    }
}
