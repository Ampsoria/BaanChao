using Microsoft.EntityFrameworkCore;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;
using Xunit;

namespace RentalManager.Tests;

public sealed class SqlServerIntegrationTests
{
    [Fact]
    public async Task Migrations_CreateSeedViewAndStoredProcedures()
    {
        var connectionString = Environment.GetEnvironmentVariable("RENTAL_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Skip("Set RENTAL_TEST_SQLSERVER to run SQL Server integration tests.");

        var options = new DbContextOptionsBuilder<RentalDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new RentalDbContext(options);
        var ct = TestContext.Current.CancellationToken;
        await db.Database.MigrateAsync(ct);

        Assert.Equal(6, await db.Rooms.CountAsync(ct));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.views WHERE name = 'vw_InvoiceStatus'").SingleAsync(ct));
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.procedures WHERE name IN ('sp_GenerateMonthlyInvoices','sp_CreateMoveInInvoice','sp_CreateMoveOutSettlement')").SingleAsync(ct));
        Assert.Contains("i.TotalAmount - ISNULL(p.PaidAmount, 0) + r.PayeeCents",
            await db.Database.SqlQueryRaw<string>(
                "SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.vw_InvoiceStatus')) AS [Value]").SingleAsync(ct));
        await db.Database.ExecuteSqlRawAsync("EXEC dbo.sp_GenerateMonthlyInvoices @BillingPeriod = {0}", ["2026-09"], ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tenant = new Tenant
        {
            RoomId = 6,
            FullName = "SQL integration",
            MovedInAt = new DateOnly(2026, 9, 17),
            DepositAmount = 1800,
            DepositReceivedAt = new DateOnly(2026, 9, 17),
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.sp_CreateMoveInInvoice @TenantId = {0}, @WaterReading = {1}, @ElectricReading = {2}",
            [tenant.TenantId, 100m, 500m], ct);
        var invoice = await db.Invoices.SingleAsync(x => x.TenantId == tenant.TenantId, ct);
        Assert.Equal(840m, invoice.TotalAmount);
        Assert.Equal(1, await db.MeterReadings.CountAsync(x => x.RoomId == 6 && x.BillingPeriod == "2026-09", ct));

        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.InvoiceId,
            PaidAmount = 100,
            PaidAt = DateTime.UtcNow,
            Method = "Cash",
            VerificationStatus = "Verified",
            VerifiedBy = "IntegrationTest"
        });
        await db.SaveChangesAsync(ct);
        Assert.Equal(740.06m, await db.Database.SqlQuery<decimal>(
            $"SELECT CAST(TransferAmount AS decimal(10,2)) AS [Value] FROM dbo.vw_InvoiceStatus WHERE InvoiceId = {invoice.InvoiceId}")
            .SingleAsync(ct));

        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.sp_CreateMoveOutSettlement @TenantId = {0}, @MoveOutDate = {1}, @WaterFinal = {2}, @ElectricFinal = {3}",
            [tenant.TenantId, new DateOnly(2026, 9, 30), 100m, 500m], ct);
        var settlement = await db.MoveOutSettlements.SingleAsync(x => x.TenantId == tenant.TenantId, ct);
        Assert.Equal(740m, settlement.OutstandingAmount);
        Assert.True(settlement.IsForfeited);
        await transaction.RollbackAsync(ct);
    }
}
