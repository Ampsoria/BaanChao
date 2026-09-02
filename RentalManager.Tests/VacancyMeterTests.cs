using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentalManager.Core.Entities;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;
using Xunit;

namespace RentalManager.Tests;

public sealed class VacancyMeterTests
{
    [Fact]
    public async Task MoveOutThenMoveInAfterVacancy_StoresBothBoundariesAndDoesNotBillTheGap()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        var oldTenant = new Tenant
        {
            RoomId = 2,
            FullName = "ผู้เช่าคนเก่า",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 2_000m,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(oldTenant);
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-08",
            ReadAt = new DateOnly(2026, 8, 31),
            WaterPrev = 90m,
            WaterCurrent = 100m,
            ElectricPrev = 480m,
            ElectricCurrent = 500m
        });
        await db.SaveChangesAsync(ct);
        var service = NewService(db);

        await service.MoveOutAsync(
            new MoveOutCommand(oldTenant.TenantId, new DateOnly(2026, 9, 1), 110m, 510m, []), "test", ct);
        var moveIn = await service.MoveInAsync(
            new MoveInCommand(2, "ผู้เช่าคนใหม่", null, new DateOnly(2026, 11, 1), 113m, 515m), "test", ct);

        var checkpoints = await db.MeterCheckpoints.OrderBy(x => x.RecordedAt).ToListAsync(ct);
        Assert.Collection(checkpoints,
            point =>
            {
                Assert.Equal(MeterCheckpointKinds.MoveOut, point.Kind);
                Assert.Equal(110m, point.WaterReading);
                Assert.Equal(510m, point.ElectricReading);
            },
            point =>
            {
                Assert.Equal(MeterCheckpointKinds.MoveIn, point.Kind);
                Assert.Equal(113m, point.WaterReading);
                Assert.Equal(515m, point.ElectricReading);
            });
        Assert.Contains("ช่วงห้องว่าง: น้ำ 3.00, ไฟ 5.00 (ไม่คิดผู้เช่า)", moveIn.Message);

        var incomingInvoice = await db.Invoices.SingleAsync(x => x.TenantId == moveIn.Value, ct);
        Assert.Null(incomingInvoice.UtilityPeriod);
        Assert.Equal(0m, incomingInvoice.WaterUnits);
        Assert.Equal(0m, incomingInvoice.ElectricUnits);
        Assert.DoesNotContain(await db.MeterReadings.ToListAsync(ct), x => x.BillingPeriod == "2026-11");

        await service.AddMeterReadingAsync(
            new MeterReadingCommand(2, "2026-11", new DateOnly(2026, 11, 30), 118m, 525m), "test", ct);
        var monthEnd = await db.MeterReadings.SingleAsync(x => x.BillingPeriod == "2026-11", ct);
        Assert.Equal(113m, monthEnd.WaterPrev);
        Assert.Equal(118m, monthEnd.WaterCurrent);
        Assert.Equal(515m, monthEnd.ElectricPrev);
        Assert.Equal(525m, monthEnd.ElectricCurrent);
    }

    [Fact]
    public async Task MoveIn_RejectsReadingBelowTheMoveOutBoundary()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        var previousTenant = new Tenant
        {
            RoomId = 2,
            FullName = "ผู้เช่าคนเก่า",
            MovedInAt = new DateOnly(2026, 1, 1),
            MovedOutAt = new DateOnly(2026, 8, 31),
            DepositAmount = 2_000m
        };
        db.Tenants.Add(previousTenant);
        await db.SaveChangesAsync(ct);
        db.MeterCheckpoints.Add(new MeterCheckpoint
        {
            RoomId = 2,
            TenantId = previousTenant.TenantId,
            RecordedAt = new DateOnly(2026, 8, 31),
            Kind = MeterCheckpointKinds.MoveOut,
            WaterReading = 110m,
            ElectricReading = 510m
        });
        await db.SaveChangesAsync(ct);

        var error = await Assert.ThrowsAsync<RentalOperationException>(() => NewService(db).MoveInAsync(
            new MoveInCommand(2, "ผู้เช่าคนใหม่", null, new DateOnly(2026, 11, 1), 109m, 515m), "test", ct));

        Assert.Contains("ต้องไม่น้อยกว่าเลขล่าสุด", error.Message);
        Assert.Single(await db.Tenants.ToListAsync(ct));
    }

    [Fact]
    public async Task TenantCanChangeWithinTheSameMonthWithoutAConflictingMonthlyReading()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        var oldTenant = new Tenant
        {
            RoomId = 2,
            FullName = "ผู้เช่าคนเก่า",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 2_000m
        };
        db.Tenants.Add(oldTenant);
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-08",
            ReadAt = new DateOnly(2026, 8, 31),
            WaterPrev = 90m,
            WaterCurrent = 100m,
            ElectricPrev = 480m,
            ElectricCurrent = 500m
        });
        await db.SaveChangesAsync(ct);
        var service = NewService(db);

        await service.MoveOutAsync(
            new MoveOutCommand(oldTenant.TenantId, new DateOnly(2026, 9, 10), 110m, 510m, []), "test", ct);
        var incoming = await service.MoveInAsync(
            new MoveInCommand(2, "ผู้เช่าคนใหม่", null, new DateOnly(2026, 9, 11), 111m, 512m), "test", ct);

        Assert.True(incoming.Value > 0);
        Assert.Equal(2, await db.MeterCheckpoints.CountAsync(ct));
        Assert.DoesNotContain(await db.MeterReadings.ToListAsync(ct), x => x.BillingPeriod == "2026-09");
    }

    [Fact]
    public async Task MoveOut_ChargesFromTheLatestMoveInCheckpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        var tenant = new Tenant
        {
            RoomId = 2,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 9, 1),
            DepositAmount = 2_000m,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-08",
            ReadAt = new DateOnly(2026, 8, 31),
            WaterPrev = 90m,
            WaterCurrent = 100m,
            ElectricPrev = 480m,
            ElectricCurrent = 500m
        });
        await db.SaveChangesAsync(ct);
        db.MeterCheckpoints.Add(new MeterCheckpoint
        {
            RoomId = 2,
            TenantId = tenant.TenantId,
            RecordedAt = new DateOnly(2026, 9, 1),
            Kind = MeterCheckpointKinds.MoveIn,
            WaterReading = 105m,
            ElectricReading = 505m
        });
        await db.SaveChangesAsync(ct);

        await NewService(db).MoveOutAsync(
            new MoveOutCommand(tenant.TenantId, new DateOnly(2026, 9, 2), 106m, 507m, []), "test", ct);

        var settlement = await db.MoveOutSettlements.SingleAsync(ct);
        Assert.Equal(20m, settlement.FinalWaterAmount);
        Assert.Equal(24m, settlement.FinalElectricAmount);
    }

    private static RentalOperationsService NewService(RentalDbContext db) =>
        new(db, Options.Create(new BillingOptions { DueDay = 5, MinimumStayMonths = 5 }));

    private static async Task<RentalDbContext> SeededAsync(CancellationToken ct)
    {
        var db = new RentalDbContext(new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"vacancy-meter-{Guid.NewGuid():N}")
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        db.Rooms.Add(new Room { RoomId = 2, RoomNumber = "2", MonthlyRent = 2_000m, PayeeCents = .02m });
        db.UtilityRates.Add(new UtilityRate
        {
            EffectiveFrom = new DateOnly(2026, 1, 1),
            WaterPerUnit = 20m,
            ElectricPerUnit = 12m,
            TrashPerMonth = 40m
        });
        db.BillingPolicies.Add(new BillingPolicy
        {
            EffectiveFrom = new DateOnly(2026, 1, 1),
            GraceDays = 5,
            LateFeeType = LateFeeType.None
        });
        await db.SaveChangesAsync(ct);
        return db;
    }
}
