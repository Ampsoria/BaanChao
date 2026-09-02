using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Controllers;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;
using Xunit;

namespace RentalManager.Tests;

public sealed class ReportExportTests
{
    [Fact]
    public async Task TenantCsv_HasUtf8BomAndNeutralizesSpreadsheetFormula()
    {
        await using var db = new RentalDbContext(new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"report-{Guid.NewGuid():N}").Options);
        db.Database.EnsureCreated();
        db.Tenants.Add(new Tenant
        {
            RoomId = 1,
            FullName = "=DANGEROUS()",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 1_800m
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ReportsController(db).Tenants(TestContext.Current.CancellationToken);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.FileContents[..3]);
        var text = Encoding.UTF8.GetString(file.FileContents[3..]);
        Assert.Contains("\"'=DANGEROUS()\"", text);
        Assert.Equal("tenants.csv", file.FileDownloadName);
    }

    [Fact]
    public async Task InvoiceCsv_KeepsNegativeAdjustmentAsANumber()
    {
        await using var db = new RentalDbContext(new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"report-{Guid.NewGuid():N}").Options);
        db.Database.EnsureCreated();
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 1_800m
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Invoices.Add(new Invoice
        {
            RoomId = 1,
            TenantId = tenant.TenantId,
            BillingPeriod = "2026-09",
            IssuedAt = new DateTime(2026, 9, 1),
            DueDate = new DateOnly(2026, 9, 5),
            PeriodStart = new DateOnly(2026, 9, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            DaysCharged = 30,
            DaysInPeriod = 30,
            RentAmount = 1_800m,
            AdjustmentAmount = -25m
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ReportsController(db).Invoices(null, TestContext.Current.CancellationToken);

        var file = Assert.IsType<FileContentResult>(result);
        var text = Encoding.UTF8.GetString(file.FileContents[3..]);
        Assert.Contains("\"-25.00\"", text);
        Assert.DoesNotContain("\"'-25.00\"", text);
    }

    [Fact]
    public async Task MeterCheckpointCsv_ExportsMoveInAndMoveOutBoundaries()
    {
        await using var db = new RentalDbContext(new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"report-{Guid.NewGuid():N}").Options);
        db.Database.EnsureCreated();
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2026, 1, 1),
            DepositAmount = 1_800m
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.MeterCheckpoints.Add(new MeterCheckpoint
        {
            RoomId = 1,
            TenantId = tenant.TenantId,
            RecordedAt = new DateOnly(2026, 9, 1),
            Kind = MeterCheckpointKinds.MoveOut,
            WaterReading = 520m,
            ElectricReading = 8_958m
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new ReportsController(db).MeterCheckpoints(TestContext.Current.CancellationToken);

        var file = Assert.IsType<FileContentResult>(result);
        var text = Encoding.UTF8.GetString(file.FileContents[3..]);
        Assert.Contains("\"MoveOut\"", text);
        Assert.Contains("\"520.00\"", text);
        Assert.Contains("\"8958.00\"", text);
        Assert.Equal("meter-checkpoints.csv", file.FileDownloadName);
    }
}
