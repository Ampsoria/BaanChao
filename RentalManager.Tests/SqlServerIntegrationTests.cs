using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;
using Xunit;

namespace RentalManager.Tests;

/// <summary>
/// เทสที่ต้องใช้ SQL Server จริง เพราะตรวจสิ่งที่ฐานข้อมูลเป็นคนบังคับ:
/// computed column, check constraint, unique index, view และ stored procedure
/// ตั้ง RENTAL_TEST_SQLSERVER เพื่อรัน (CI ตั้งให้อัตโนมัติ) ไม่ตั้ง = ข้าม
/// ทุกเทสทำงานในทรานแซกชันแล้ว rollback จึงไม่ทิ้งข้อมูลค้าง
/// </summary>
public sealed class SqlServerIntegrationTests
{
    private static async Task<RentalDbContext?> OpenAsync(CancellationToken ct)
    {
        var connectionString = Environment.GetEnvironmentVariable("RENTAL_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("Set RENTAL_TEST_SQLSERVER to run SQL Server integration tests.");
            return null;
        }

        var options = new DbContextOptionsBuilder<RentalDbContext>().UseSqlServer(connectionString).Options;
        var db = new RentalDbContext(options);
        await MigrateWithRetryAsync(db, ct);
        return db;
    }

    /// <summary>
    /// เซิร์ฟเวอร์ใน CI อาจเพิ่งบูตเสร็จและยังไม่รับ connection ตอนเทสตัวแรกเรียก
    /// จึงลองใหม่สักพักก่อนยอมแพ้ ไม่ใช้ EnableRetryOnFailure เพราะเทสเปิดทรานแซกชันเอง
    /// ซึ่ง execution strategy แบบ retry ไม่รองรับ
    /// </summary>
    private static async Task MigrateWithRetryAsync(RentalDbContext db, CancellationToken ct)
    {
        const int attempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                return;
            }
            catch (SqlException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    [Fact]
    public async Task Migrations_CreateSeedViewAndStoredProcedures()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;

        Assert.Equal(6, await db.Rooms.CountAsync(ct));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.views WHERE name = 'vw_InvoiceStatus'").SingleAsync(ct));
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.procedures WHERE name IN ('sp_GenerateMonthlyInvoices','sp_CreateMoveInInvoice','sp_CreateMoveOutSettlement')").SingleAsync(ct));

        var viewDefinition = await db.Database.SqlQueryRaw<string>(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.vw_InvoiceStatus')) AS [Value]").SingleAsync(ct);
        Assert.Contains("i.TotalAmount - ISNULL(p.PaidAmount, 0) + r.PayeeCents", viewDefinition);
        // ยอดค้างต้องนับเฉพาะเงินที่ยืนยันแล้ว ให้ตรงกับที่ฝั่ง C# คิด
        Assert.Contains("VerificationStatus = 'Verified'", viewDefinition);
        Assert.Contains("UtilityPeriod", viewDefinition);

        // ห้องละหนึ่งเศษสตางค์ ใช้ระบุผู้โอนจากยอดในสเตทเมนต์
        var cents = await db.Rooms.OrderBy(x => x.RoomNumber).Select(x => x.PayeeCents).ToListAsync(ct);
        Assert.Equal([.01m, .02m, .03m, .04m, .05m, .06m], cents);
    }

    [Fact]
    public async Task MoveInThenMoveOut_ProducesProratedInvoiceAndForfeitedSettlement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
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

        // ค่าเช่า 1800 × 14/30 ปัดลง = 840 ไม่มีค่าน้ำ-ค่าไฟ ไม่มีค่าขยะ
        Assert.Equal(840m, invoice.TotalAmount);
        Assert.Null(invoice.UtilityPeriod);
        Assert.True(invoice.IsProrated);
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
        // ริบแล้วต้องไม่คืน และหนี้ค้างยังไม่เกินมัดจำจึงไม่มียอดเก็บเพิ่ม
        Assert.Equal(0m, settlement.RefundAmount);
        Assert.Equal(0m, settlement.AmountDueFromTenant);
        Assert.Equal(1060m, settlement.ForfeitedAmount);
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task GenerateMonthlyInvoices_UsesPreviousMonthMeterAndStaysIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            RoomId = 5,
            FullName = "งวดมิเตอร์",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 5,
            BillingPeriod = "2026-09",
            ReadAt = new DateOnly(2026, 9, 30),
            WaterPrev = 100m,
            WaterCurrent = 105m,
            ElectricPrev = 500m,
            ElectricCurrent = 530m
        });
        await db.SaveChangesAsync(ct);

        await db.Database.ExecuteSqlRawAsync("EXEC dbo.sp_GenerateMonthlyInvoices @BillingPeriod = {0}", ["2026-10"], ct);
        await db.Database.ExecuteSqlRawAsync("EXEC dbo.sp_GenerateMonthlyInvoices @BillingPeriod = {0}", ["2026-10"], ct);

        // สั่งซ้ำต้องไม่ได้บิลใบที่สอง
        var invoices = await db.Invoices.Where(x => x.TenantId == tenant.TenantId).ToListAsync(ct);
        var invoice = Assert.Single(invoices);
        Assert.Equal("2026-10", invoice.BillingPeriod);
        Assert.Equal("2026-09", invoice.UtilityPeriod);
        Assert.Equal(5m, invoice.WaterUnits);
        Assert.Equal(30m, invoice.ElectricUnits);
        // computed column: 2000 + (5×20) + (30×12) + 40 = 2500
        Assert.Equal(100m, invoice.WaterAmount);
        Assert.Equal(360m, invoice.ElectricAmount);
        Assert.Equal(2500m, invoice.TotalAmount);
        Assert.False(invoice.IsProrated);
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task VoidedInvoice_AllowsAReplacementForTheSameTenantAndPeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            RoomId = 3,
            FullName = "ออกบิลใหม่",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2_200m,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.sp_GenerateMonthlyInvoices @BillingPeriod = {0}", ["2026-10"], ct);
        var original = await db.Invoices.SingleAsync(x => x.TenantId == tenant.TenantId, ct);
        original.Status = InvoiceStatus.Void;
        await db.SaveChangesAsync(ct);

        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.sp_GenerateMonthlyInvoices @BillingPeriod = {0}", ["2026-10"], ct);

        var invoices = await db.Invoices.Where(x => x.TenantId == tenant.TenantId)
            .OrderBy(x => x.InvoiceId).ToListAsync(ct);
        Assert.Equal(2, invoices.Count);
        Assert.Equal(InvoiceStatus.Void, invoices[0].Status);
        Assert.Equal(InvoiceStatus.Unpaid, invoices[1].Status);
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task Invoice_RejectsUtilityPeriodThatIsNotTheMonthBeforeBillingPeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            RoomId = 4,
            FullName = "งวดผิด",
            MovedInAt = new DateOnly(2026, 8, 1),
            DepositAmount = 2000,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        db.Invoices.Add(new Invoice
        {
            RoomId = 4,
            TenantId = tenant.TenantId,
            BillingPeriod = "2026-10",
            UtilityPeriod = "2026-10",
            DueDate = new DateOnly(2026, 10, 5),
            PeriodStart = new DateOnly(2026, 10, 1),
            PeriodEnd = new DateOnly(2026, 10, 31),
            DaysCharged = 31,
            DaysInPeriod = 31,
            RentAmount = 2000m,
            WaterUnits = 0,
            WaterRate = 20m,
            ElectricUnits = 0,
            ElectricRate = 12m,
            TrashAmount = 40m
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        Assert.Contains("CK_Invoice_UtilityPeriod", (error.InnerException as SqlException)?.Message ?? error.Message);
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task Payments_RejectDuplicateSlipAndTrackPartialSettlement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var tenant = new Tenant
        {
            RoomId = 3,
            FullName = "จ่ายบางส่วน",
            MovedInAt = new DateOnly(2026, 9, 1),
            DepositAmount = 2200,
            MinimumStayMonths = 5
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        var invoice = new Invoice
        {
            RoomId = 3,
            TenantId = tenant.TenantId,
            BillingPeriod = "2026-09",
            DueDate = new DateOnly(2026, 9, 5),
            PeriodStart = new DateOnly(2026, 9, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            DaysCharged = 30,
            DaysInPeriod = 30,
            RentAmount = 2200m,
            WaterUnits = 0,
            WaterRate = 20m,
            ElectricUnits = 0,
            ElectricRate = 12m,
            TrashAmount = 40m
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        Assert.Equal(2240m, invoice.TotalAmount);

        var slipHash = new string('a', 64);
        // จ่ายเงินสดโดยไม่มีสลิปต้องผ่านได้
        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.InvoiceId,
            PaidAmount = 1000m,
            PaidAt = DateTime.UtcNow,
            Method = "Cash",
            VerificationStatus = "Verified",
            VerifiedBy = "Manual"
        });
        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.InvoiceId,
            PaidAmount = 240m,
            PaidAt = DateTime.UtcNow,
            Method = "PromptPay",
            SlipHash = slipHash,
            VerificationStatus = "Verified",
            VerifiedBy = "Auto"
        });
        await db.SaveChangesAsync(ct);

        var status = await db.Database.SqlQuery<string>(
            $"SELECT StatusText AS [Value] FROM dbo.vw_InvoiceStatus WHERE InvoiceId = {invoice.InvoiceId}").SingleAsync(ct);
        Assert.Equal("ชำระบางส่วน", status);
        Assert.Equal(1000m, await db.Database.SqlQuery<decimal>(
            $"SELECT CAST(Outstanding AS decimal(10,2)) AS [Value] FROM dbo.vw_InvoiceStatus WHERE InvoiceId = {invoice.InvoiceId}")
            .SingleAsync(ct));
        // ยอดที่ต้องโอนต้องมีเศษสตางค์ประจำห้อง 3 ต่อท้าย
        Assert.Equal(1000.03m, await db.Database.SqlQuery<decimal>(
            $"SELECT CAST(TransferAmount AS decimal(10,2)) AS [Value] FROM dbo.vw_InvoiceStatus WHERE InvoiceId = {invoice.InvoiceId}")
            .SingleAsync(ct));

        // ส่งสลิปเดิมซ้ำต้องถูกปฏิเสธด้วย unique index
        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.InvoiceId,
            PaidAmount = 100m,
            PaidAt = DateTime.UtcNow,
            Method = "PromptPay",
            SlipHash = slipHash,
            VerificationStatus = "Verified"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task MeterRollover_IsBlockedByCheckConstraint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.MeterReadings.Add(new MeterReading
        {
            RoomId = 1,
            BillingPeriod = "2026-07",
            ReadAt = new DateOnly(2026, 7, 31),
            WaterPrev = 100m,
            WaterCurrent = 90m,
            ElectricPrev = 500m,
            ElectricCurrent = 520m
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        Assert.Contains("CK_Water_NotNegative", (error.InnerException as SqlException)?.Message ?? error.Message);
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task Tenant_RejectsUnknownChannelAndSecondActiveOccupant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await OpenAsync(ct);
        if (db is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.Tenants.Add(new Tenant
        {
            RoomId = 2,
            FullName = "ช่องทางผิด",
            MovedInAt = new DateOnly(2026, 9, 1),
            DepositAmount = 2000,
            PreferredChannel = "Email"
        });
        var channelError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        Assert.Contains("CK_Tenant_Channel", (channelError.InnerException as SqlException)?.Message ?? channelError.Message);
        db.ChangeTracker.Clear();

        db.Tenants.Add(new Tenant
        {
            RoomId = 2,
            FullName = "คนแรก",
            MovedInAt = new DateOnly(2026, 9, 1),
            DepositAmount = 2000
        });
        await db.SaveChangesAsync(ct);
        db.Tenants.Add(new Tenant
        {
            RoomId = 2,
            FullName = "คนที่สอง",
            MovedInAt = new DateOnly(2026, 9, 2),
            DepositAmount = 2000
        });
        // filtered unique index บังคับว่าหนึ่งห้องมีผู้เช่า active ได้คนเดียว
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        await transaction.RollbackAsync(ct);
    }
}
