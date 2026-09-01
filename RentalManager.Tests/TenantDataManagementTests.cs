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

public sealed class TenantDataManagementTests
{
    [Fact]
    public async Task ImportExistingTenant_CreatesTenantAndBaselineWithoutAnInvoice()
    {
        await using var db = CreateDatabase();
        var service = CreateService(db);
        var ct = TestContext.Current.CancellationToken;

        var result = await service.ImportExistingTenantAsync(new ImportExistingTenantCommand(
            1, "ผู้เช่าเดิม", "0812345678", new DateOnly(2024, 2, 10),
            1_500m, new DateOnly(2024, 2, 10), 5,
            "2026-08", new DateOnly(2026, 8, 31), 120m, 2_503m), "test", ct);

        var tenant = await db.Tenants.SingleAsync(ct);
        var meter = await db.MeterReadings.SingleAsync(ct);
        Assert.Equal(tenant.TenantId, result.Value);
        Assert.Equal(1_500m, tenant.DepositAmount);
        Assert.Equal(TenantChannels.Paper, tenant.PreferredChannel);
        Assert.Equal(meter.WaterPrev, meter.WaterCurrent);
        Assert.Equal(meter.ElectricPrev, meter.ElectricCurrent);
        Assert.Empty(await db.Invoices.ToListAsync(ct));
        Assert.Contains(await db.AuditLogs.ToListAsync(ct), x => x.FieldName == "ImportExisting");
    }

    [Fact]
    public async Task ImportExistingTenant_ReusesMatchingSavedBaseline()
    {
        await using var db = CreateDatabase();
        var ct = TestContext.Current.CancellationToken;
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 1,
            BillingPeriod = "2026-08",
            ReadAt = new DateOnly(2026, 8, 31),
            WaterPrev = 366m,
            WaterCurrent = 366m,
            ElectricPrev = 6_387m,
            ElectricCurrent = 6_387m
        });
        await db.SaveChangesAsync(ct);

        var result = await CreateService(db).ImportExistingTenantAsync(new ImportExistingTenantCommand(
            1, "ผู้เช่าเดิม", null, new DateOnly(2024, 1, 1), 1_800m, null, 5,
            "2026-08", new DateOnly(2026, 8, 31), 366m, 6_387m), "test", ct);

        Assert.Contains("ใช้เลขมิเตอร์ตั้งต้นที่มีอยู่", result.Message);
        Assert.Equal(1, await db.MeterReadings.CountAsync(ct));
        Assert.Equal(1, await db.Tenants.CountAsync(ct));
        Assert.Empty(await db.Invoices.ToListAsync(ct));
    }

    [Fact]
    public async Task UpdateTenant_ChangesProfileAndDeposit()
    {
        await using var db = CreateDatabase();
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ชื่อเดิม",
            Phone = "0800000000",
            MovedInAt = new DateOnly(2024, 1, 1),
            DepositAmount = 1_800m,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).UpdateTenant(tenant.TenantId,
            new UpdateTenantRequest("ชื่อใหม่", "0899999999", new DateOnly(2024, 1, 1),
                2_000m, new DateOnly(2024, 1, 1), 6), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("ชื่อใหม่", tenant.FullName);
        Assert.Equal("0899999999", tenant.Phone);
        Assert.Equal(2_000m, tenant.DepositAmount);
        Assert.Equal(6, tenant.MinimumStayMonths);
    }

    [Fact]
    public async Task UpdateTenant_RejectsMoveInDateChangeAfterBilling()
    {
        await using var db = CreateDatabase();
        var tenant = AddTenantAndInvoice(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).UpdateTenant(tenant.TenantId,
            new UpdateTenantRequest(tenant.FullName, tenant.Phone, new DateOnly(2024, 2, 1),
                tenant.DepositAmount, tenant.DepositReceivedAt, tenant.MinimumStayMonths),
            TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(new DateOnly(2024, 1, 1), tenant.MovedInAt);
    }

    private static Tenant AddTenantAndInvoice(RentalDbContext db)
    {
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2024, 1, 1),
            DepositAmount = 1_800m,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        db.Invoices.Add(new Invoice
        {
            RoomId = 1,
            TenantId = tenant.TenantId,
            BillingPeriod = "2026-09",
            DueDate = new DateOnly(2026, 9, 5),
            PeriodStart = new DateOnly(2026, 9, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            DaysCharged = 30,
            DaysInPeriod = 30,
            RentAmount = 1_800m,
            WaterRate = 20m,
            ElectricRate = 12m,
            TrashAmount = 40m
        });
        return tenant;
    }

    private static RentalDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"tenant-data-{Guid.NewGuid():N}")
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new RentalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RentalOperationsService CreateService(RentalDbContext db) =>
        new(db, Options.Create(new BillingOptions { DueDay = 5, MinimumStayMonths = 5 }));

    private static TenantsController CreateController(RentalDbContext db)
    {
        var controller = new TenantsController(CreateService(db), db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "TenantDataTest")], "Test"))
            }
        };
        return controller;
    }
}
