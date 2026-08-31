using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentalManager.Core.Entities;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;
using Xunit;

namespace RentalManager.Tests;

/// <summary>
/// ตรวจกฎการออกบิลรายเดือนตาม CLAUDE.md ข้อ 4 หัวข้อ "รอบจดมิเตอร์"
/// ใช้ EF InMemory เพราะที่ตรวจคือการตัดสินใจในโค้ด (เลือกงวดมิเตอร์ / ออกซ้ำ / คิดหน่วยให้ใคร)
/// ส่วนที่เป็นข้อกำหนดของฐานข้อมูลเอง (computed column, constraint, stored procedure)
/// อยู่ใน SqlServerIntegrationTests ซึ่งต้องใช้ SQL Server จริง
/// </summary>
public sealed class InvoiceGenerationTests
{
    private static RentalDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"invoices-{Guid.NewGuid():N}")
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static RentalOperationsService NewService(RentalDbContext db) =>
        new(db, Options.Create(new BillingOptions { DueDay = 5, MinimumStayMonths = 5 }));

    private static async Task<RentalDbContext> SeededAsync(CancellationToken ct)
    {
        var db = NewContext();
        db.Rooms.Add(new Room { RoomId = 2, RoomNumber = "2", MonthlyRent = 2000m, PayeeCents = .02m });
        db.UtilityRates.Add(new UtilityRate
        {
            RateId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            WaterPerUnit = 20m,
            ElectricPerUnit = 12m,
            TrashPerMonth = 40m
        });
        db.BillingPolicies.Add(new BillingPolicy
        {
            PolicyId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            GraceDays = 5,
            LateFeeType = LateFeeType.None
        });
        await db.SaveChangesAsync(ct);
        return db;
    }

    [Theory]
    [InlineData("2026-10", "2026-09")]
    [InlineData("2026-01", "2025-12")]
    [InlineData("2026-03", "2026-02")]
    public void UtilityPeriod_IsAlwaysTheMonthBeforeBillingPeriod(string billing, string expected) =>
        Assert.Equal(expected, RentalOperationsService.PreviousPeriod(billing));

    [Fact]
    public async Task MonthlyInvoice_ChargesUtilitiesOfThePreviousMonth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        db.Tenants.Add(new Tenant
        {
            TenantId = 1,
            RoomId = 2,
            FullName = "ผู้เช่าเดิม",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000m
        });
        // เดินจดมิเตอร์วันที่ 30 ก.ย. → เป็นของงวด '2026-09'
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-09",
            ReadAt = new DateOnly(2026, 9, 30),
            WaterPrev = 100m,
            WaterCurrent = 105m,
            ElectricPrev = 500m,
            ElectricCurrent = 530m
        });
        // งวด ต.ค. ยังไม่ได้จด — บิลต้องไม่ไปหยิบมาใช้
        await db.SaveChangesAsync(ct);

        var result = await NewService(db).GenerateMonthlyInvoicesAsync("2026-10", "test", ct);

        Assert.Equal(1, result.Value);
        var invoice = await db.Invoices.SingleAsync(ct);
        Assert.Equal("2026-10", invoice.BillingPeriod);
        Assert.Equal("2026-09", invoice.UtilityPeriod);
        Assert.Equal(5m, invoice.WaterUnits);
        Assert.Equal(30m, invoice.ElectricUnits);
        Assert.Equal(2000m, invoice.RentAmount);
        Assert.Equal(40m, invoice.TrashAmount);
        Assert.Equal(new DateOnly(2026, 10, 5), invoice.DueDate);
    }

    [Fact]
    public async Task RegeneratingTheSamePeriod_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        db.Tenants.Add(new Tenant
        {
            TenantId = 1,
            RoomId = 2,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000m
        });
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-09",
            ReadAt = new DateOnly(2026, 9, 30),
            WaterPrev = 100m,
            WaterCurrent = 105m,
            ElectricPrev = 500m,
            ElectricCurrent = 530m
        });
        await db.SaveChangesAsync(ct);
        var service = NewService(db);

        Assert.Equal(1, (await service.GenerateMonthlyInvoicesAsync("2026-10", "test", ct)).Value);
        var second = await service.GenerateMonthlyInvoicesAsync("2026-10", "test", ct);

        Assert.Equal(0, second.Value);
        Assert.Equal(1, await db.Invoices.CountAsync(ct));
    }

    [Fact]
    public async Task RoomThatChangedTenantMidMonth_GetsTwoInvoicesAndNoInheritedUtilities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        // คนเก่าย้ายออก 10 ต.ค. คนใหม่เข้า 11 ต.ค.
        db.Tenants.AddRange(
            new Tenant
            {
                TenantId = 1,
                RoomId = 2,
                FullName = "คนเก่า",
                MovedInAt = new DateOnly(2026, 5, 1),
                MovedOutAt = new DateOnly(2026, 10, 10),
                DepositAmount = 2000m
            },
            new Tenant
            {
                TenantId = 2,
                RoomId = 2,
                FullName = "คนใหม่",
                MovedInAt = new DateOnly(2026, 10, 11),
                DepositAmount = 2000m
            });
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-09",
            ReadAt = new DateOnly(2026, 9, 30),
            WaterPrev = 100m,
            WaterCurrent = 105m,
            ElectricPrev = 500m,
            ElectricCurrent = 530m
        });
        await db.SaveChangesAsync(ct);

        var result = await NewService(db).GenerateMonthlyInvoicesAsync("2026-10", "test", ct);

        Assert.Equal(2, result.Value);
        var previous = await db.Invoices.SingleAsync(x => x.TenantId == 1, ct);
        var incoming = await db.Invoices.SingleAsync(x => x.TenantId == 2, ct);

        // คนเก่าอยู่มาตลอดงวด ก.ย. → รับค่าน้ำ-ค่าไฟไป และค่าเช่าคิดเต็มเดือนตอนย้ายออก
        Assert.Equal("2026-09", previous.UtilityPeriod);
        Assert.Equal(5m, previous.WaterUnits);
        Assert.Equal(2000m, previous.RentAmount);

        // คนใหม่ต้องไม่โดนหน่วยของคนก่อน และค่าเช่าเฉลี่ยตามวัน 21/31 วัน
        Assert.Null(incoming.UtilityPeriod);
        Assert.Equal(0m, incoming.WaterUnits);
        Assert.Equal(0m, incoming.ElectricUnits);
        Assert.Equal(decimal.Floor(2000m * 21 / 31), incoming.RentAmount);
        Assert.Equal(0m, incoming.TrashAmount);
    }

    [Fact]
    public async Task MissingPreviousReading_StillBillsRentAndSaysSoInsteadOfSkippingSilently()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        db.Tenants.Add(new Tenant
        {
            TenantId = 1,
            RoomId = 2,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000m
        });
        await db.SaveChangesAsync(ct);

        var result = await NewService(db).GenerateMonthlyInvoicesAsync("2026-10", "test", ct);

        Assert.Equal(1, result.Value);
        Assert.Contains("2026-09", result.Message);
        Assert.Contains("ห้อง 2", result.Message);
        var invoice = await db.Invoices.SingleAsync(ct);
        Assert.Null(invoice.UtilityPeriod);
        Assert.Equal(0m, invoice.WaterUnits);
        Assert.Equal(2000m, invoice.RentAmount);
    }

    [Fact]
    public async Task CatchUpGeneration_StillBillsAMonthWhoseFirstDayWasMissed()
    {
        // จำลองแอปที่หลับข้ามวันที่ 1 ต.ค. แล้วมาตื่นวันที่ 3
        // การตามเก็บต้องยังออกบิลงวด ต.ค. ให้ครบ ไม่ใช่ปล่อยให้เดือนนั้นหายไป
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        db.Tenants.Add(new Tenant
        {
            TenantId = 1,
            RoomId = 2,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000m
        });
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 2,
            BillingPeriod = "2026-09",
            ReadAt = new DateOnly(2026, 9, 30),
            WaterPrev = 100m,
            WaterCurrent = 105m,
            ElectricPrev = 500m,
            ElectricCurrent = 530m
        });
        await db.SaveChangesAsync(ct);

        var result = await NewService(db).GenerateMonthlyInvoicesAsync("2026-10", "Automation", ct);

        Assert.Equal(1, result.Value);
        var invoice = await db.Invoices.SingleAsync(ct);
        Assert.Equal("2026-10", invoice.BillingPeriod);
        // วันครบกำหนดยังเป็นวันที่ 5 ตามนโยบาย ไม่ได้เลื่อนตามวันที่ตามเก็บ
        Assert.Equal(new DateOnly(2026, 10, 5), invoice.DueDate);
    }

    [Fact]
    public async Task RepeatedGeneration_DoesNotFloodTheAuditLog()
    {
        // งานอัตโนมัติเรียกซ้ำทุกรอบ (ทุก 15 นาที) เพื่อตามเก็บงวดที่ตกหล่น
        // audit ต้องบันทึกเฉพาะรอบที่มีบิลเกิดจริง
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        db.Tenants.Add(new Tenant
        {
            TenantId = 1,
            RoomId = 2,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000m
        });
        await db.SaveChangesAsync(ct);
        var service = NewService(db);

        for (var i = 0; i < 5; i++)
            await service.GenerateMonthlyInvoicesAsync("2026-10", "Automation", ct);

        Assert.Equal(1, await db.Invoices.CountAsync(ct));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.FieldName == "GenerateMonthly", ct));
    }

    [Fact]
    public async Task MoveInInvoice_HasNoUtilityPeriodAndUsesConfiguredMinimumStay()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        var service = new RentalOperationsService(
            db, Options.Create(new BillingOptions { DueDay = 5, MinimumStayMonths = 7 }));

        await service.MoveInAsync(
            new MoveInCommand(2, "ผู้เช่าใหม่", "0812345678", new DateOnly(2026, 9, 17), 100m, 500m, TenantChannels.Line),
            "test", ct);

        var tenant = await db.Tenants.SingleAsync(ct);
        Assert.Equal((byte)7, tenant.MinimumStayMonths);
        Assert.Equal(TenantChannels.Line, tenant.PreferredChannel);
        Assert.Equal(2000m, tenant.DepositAmount);

        var invoice = await db.Invoices.SingleAsync(ct);
        Assert.Equal("2026-09", invoice.BillingPeriod);
        Assert.Null(invoice.UtilityPeriod);
        Assert.Equal(933m, invoice.RentAmount);
        Assert.Equal(0m, invoice.TrashAmount);

        // เลขมิเตอร์ตั้งต้นต้องกลายเป็น WaterPrev ของบิลใบถัดไป
        var reading = await db.MeterReadings.SingleAsync(ct);
        Assert.Equal("2026-09", reading.BillingPeriod);
        Assert.Equal(100m, reading.WaterPrev);
        Assert.Equal(100m, reading.WaterCurrent);
    }

    [Fact]
    public async Task MoveIn_RejectsUnknownChannelAndOccupiedRoom()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await SeededAsync(ct);
        var service = NewService(db);

        await Assert.ThrowsAsync<RentalOperationException>(() => service.MoveInAsync(
            new MoveInCommand(2, "ผู้เช่า", null, new DateOnly(2026, 9, 1), 100m, 500m, "Email"), "test", ct));

        await service.MoveInAsync(
            new MoveInCommand(2, "ผู้เช่าแรก", null, new DateOnly(2026, 9, 1), 100m, 500m), "test", ct);
        var error = await Assert.ThrowsAsync<RentalOperationException>(() => service.MoveInAsync(
            new MoveInCommand(2, "ผู้เช่าสอง", null, new DateOnly(2026, 9, 5), 100m, 500m), "test", ct));
        Assert.Contains("มีผู้เช่าอยู่แล้ว", error.Message);
    }
}
