using Microsoft.EntityFrameworkCore;
using RentalManager.Core.Entities;

namespace RentalManager.Infrastructure.Data;

public sealed class RentalDbContext(DbContextOptions<RentalDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UtilityRate> UtilityRates => Set<UtilityRate>();
    public DbSet<BillingPolicy> BillingPolicies => Set<BillingPolicy>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<MeterCheckpoint> MeterCheckpoints => Set<MeterCheckpoint>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<MoveOutSettlement> MoveOutSettlements => Set<MoveOutSettlement>();
    public DbSet<SettlementDeduction> SettlementDeductions => Set<SettlementDeduction>();
    public DbSet<TenantLinkCode> TenantLinkCodes => Set<TenantLinkCode>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    /// <summary>
    /// true เมื่อรันบน SQLite ซึ่งใช้เฉพาะโหมดดูหน้าจอบนเครื่อง dev
    /// ของจริงเป็น SQL Server เสมอ — ดู README หัวข้อ "ลองรันแบบไม่ติดตั้งอะไร"
    /// </summary>
    private bool IsSqlite => Database.ProviderName?.EndsWith(".Sqlite", StringComparison.Ordinal) == true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var sqlite = IsSqlite;
        ConfigureRoom(modelBuilder, sqlite);
        ConfigureTenant(modelBuilder, sqlite);
        ConfigureRates(modelBuilder, sqlite);
        ConfigureMeterReading(modelBuilder, sqlite);
        ConfigureMeterCheckpoint(modelBuilder, sqlite);
        ConfigureInvoice(modelBuilder, sqlite);
        ConfigurePayment(modelBuilder, sqlite);
        ConfigureAuditLog(modelBuilder, sqlite);
        ConfigureSettlement(modelBuilder, sqlite);
        ConfigureMessaging(modelBuilder, sqlite);
        Seed(modelBuilder);
    }

    /// <summary>เวลาปัจจุบันแบบ UTC ตามไวยากรณ์ของแต่ละ provider</summary>
    private static string UtcNowSql(bool sqlite) => sqlite ? "CURRENT_TIMESTAMP" : "SYSUTCDATETIME()";

    /// <summary>
    /// SQLite ไม่มีชนิด decimal จริง EF จึงเก็บเป็น TEXT
    /// การเทียบตัวเลขใน CHECK constraint เลยกลายเป็นการเทียบสตริง ('0.0' = 0 ได้ false
    /// และ '9.0' >= '100.0' ได้ true) ต้อง cast ก่อนเทียบ
    /// บน SQL Server คืนค่าเดิมไม่แตะ เพื่อให้สตริงตรงกับ migration snapshot เป๊ะ
    /// </summary>
    private static string Money(bool sqlite, string column) => sqlite ? $"CAST({column} AS REAL)" : column;

    private static void ConfigureRoom(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<Room>();
        entity.ToTable("Room", table => table.HasCheckConstraint("CK_Room_Rent", $"{Money(sqlite, "[MonthlyRent]")} >= 0"));
        entity.HasKey(x => x.RoomId);
        entity.Property(x => x.RoomNumber).HasMaxLength(10).IsRequired();
        entity.Property(x => x.MonthlyRent).HasPrecision(10, 2);
        entity.Property(x => x.PayeeCents).HasPrecision(4, 2);
        entity.HasIndex(x => x.RoomNumber).IsUnique();
        entity.HasIndex(x => x.PayeeCents).IsUnique();
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<Tenant>();
        entity.ToTable("Tenant", table =>
        {
            table.HasCheckConstraint("CK_Tenant_Deposit", $"{Money(sqlite, "[DepositAmount]")} >= 0");
            table.HasCheckConstraint("CK_Tenant_Channel", "[PreferredChannel] IN ('Line','Paper')");
        });
        entity.HasKey(x => x.TenantId);
        entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.PreferredChannel).HasMaxLength(10).HasDefaultValue(TenantChannels.Paper);
        entity.Property(x => x.Phone).HasMaxLength(20);
        entity.Property(x => x.LineUserId).HasMaxLength(64);
        entity.Property(x => x.DepositAmount).HasPrecision(10, 2);
        entity.HasIndex(x => x.LineUserId).HasFilter("[LineUserId] IS NOT NULL");
        entity.HasIndex(x => x.RoomId).IsUnique().HasFilter("[MovedOutAt] IS NULL");
        entity.HasOne(x => x.Room).WithMany(x => x.Tenants).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRates(ModelBuilder modelBuilder, bool sqlite)
    {
        var rate = modelBuilder.Entity<UtilityRate>();
        rate.ToTable("UtilityRate", table => table.HasCheckConstraint(
            "CK_UtilityRate_NonNegative",
            $"{Money(sqlite, "[WaterPerUnit]")} >= 0 AND {Money(sqlite, "[ElectricPerUnit]")} >= 0 AND {Money(sqlite, "[TrashPerMonth]")} >= 0"));
        rate.HasKey(x => x.RateId);
        rate.HasIndex(x => x.EffectiveFrom).IsUnique();
        rate.Property(x => x.WaterPerUnit).HasPrecision(10, 2);
        rate.Property(x => x.ElectricPerUnit).HasPrecision(10, 2);
        rate.Property(x => x.TrashPerMonth).HasPrecision(10, 2);
        rate.Property(x => x.Note).HasMaxLength(200);

        var policy = modelBuilder.Entity<BillingPolicy>();
        policy.ToTable("BillingPolicy", table =>
        {
            table.HasCheckConstraint("CK_LateFeeType", "[LateFeeType] IN ('None','PerDay','Flat')");
            table.HasCheckConstraint("CK_LateFee_NonNegative", $"{Money(sqlite, "[LateFeeAmount]")} >= 0 AND ([LateFeeCap] IS NULL OR {Money(sqlite, "[LateFeeCap]")} >= 0)");
        });
        policy.HasKey(x => x.PolicyId);
        policy.HasIndex(x => x.EffectiveFrom).IsUnique();
        policy.Property(x => x.LateFeeType).HasConversion<string>().HasMaxLength(10);
        policy.Property(x => x.LateFeeAmount).HasPrecision(10, 2);
        policy.Property(x => x.LateFeeCap).HasPrecision(10, 2);
        policy.Property(x => x.Note).HasMaxLength(200);
    }

    private static void ConfigureMeterReading(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<MeterReading>();
        entity.ToTable("MeterReading", table =>
        {
            table.HasCheckConstraint("CK_Water_NotNegative", $"{Money(sqlite, "[WaterCurrent]")} >= {Money(sqlite, "[WaterPrev]")}");
            table.HasCheckConstraint("CK_Electric_NotNegative", $"{Money(sqlite, "[ElectricCurrent]")} >= {Money(sqlite, "[ElectricPrev]")}");
        });
        entity.HasKey(x => x.ReadingId);
        entity.Property(x => x.BillingPeriod).HasColumnType("char(7)").IsRequired();
        entity.Property(x => x.WaterPrev).HasPrecision(12, 2);
        entity.Property(x => x.WaterCurrent).HasPrecision(12, 2);
        entity.Property(x => x.ElectricPrev).HasPrecision(12, 2);
        entity.Property(x => x.ElectricCurrent).HasPrecision(12, 2);
        entity.Property(x => x.WaterUnits).HasPrecision(12, 2)
            .HasComputedColumnSql("[WaterCurrent] - [WaterPrev]", stored: true);
        entity.Property(x => x.ElectricUnits).HasPrecision(12, 2)
            .HasComputedColumnSql("[ElectricCurrent] - [ElectricPrev]", stored: true);
        entity.HasIndex(x => new { x.RoomId, x.BillingPeriod }).IsUnique();
        entity.HasOne(x => x.Room).WithMany(x => x.MeterReadings).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMeterCheckpoint(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<MeterCheckpoint>();
        entity.ToTable("MeterCheckpoint", table =>
        {
            table.HasCheckConstraint("CK_MeterCheckpoint_Water", $"{Money(sqlite, "[WaterReading]")} >= 0");
            table.HasCheckConstraint("CK_MeterCheckpoint_Electric", $"{Money(sqlite, "[ElectricReading]")} >= 0");
            table.HasCheckConstraint("CK_MeterCheckpoint_Kind", "[Kind] IN ('MoveIn','MoveOut','ImportedBaseline')");
        });
        entity.HasKey(x => x.MeterCheckpointId);
        entity.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        entity.Property(x => x.WaterReading).HasPrecision(12, 2);
        entity.Property(x => x.ElectricReading).HasPrecision(12, 2);
        entity.HasIndex(x => new { x.RoomId, x.RecordedAt });
        entity.HasIndex(x => new { x.TenantId, x.Kind }).IsUnique();
        entity.HasOne(x => x.Room).WithMany(x => x.MeterCheckpoints)
            .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Tenant).WithMany(x => x.MeterCheckpoints)
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureInvoice(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<Invoice>();
        entity.ToTable("Invoice", table =>
        {
            table.HasCheckConstraint("CK_Invoice_Days", "[DaysCharged] > 0 AND [DaysCharged] <= [DaysInPeriod]");
            table.HasCheckConstraint("CK_Invoice_Units", $"{Money(sqlite, "[WaterUnits]")} >= 0 AND {Money(sqlite, "[ElectricUnits]")} >= 0");
            // ค่าน้ำ-ค่าไฟเก็บย้อนหลังเสมอ: UtilityPeriod ต้องเป็นเดือนก่อน BillingPeriod พอดี
            table.HasCheckConstraint(
                "CK_Invoice_UtilityPeriod",
                sqlite
                    ? "[UtilityPeriod] IS NULL OR [UtilityPeriod] = "
                      + "substr(date([BillingPeriod] || '-01', '-1 month'), 1, 7)"
                    : "[UtilityPeriod] IS NULL OR [UtilityPeriod] = CONVERT(char(7), "
                      + "DATEADD(MONTH, -1, CONVERT(date, [BillingPeriod] + '-01', 126)), 126)");
            // ไม่มีเลขมิเตอร์ของงวดก่อน = ห้ามมีหน่วยน้ำ-ไฟบนบิล
            table.HasCheckConstraint(
                "CK_Invoice_UtilityUnits",
                $"[UtilityPeriod] IS NOT NULL OR ({Money(sqlite, "[WaterUnits]")} = 0 AND {Money(sqlite, "[ElectricUnits]")} = 0)");
        });
        entity.HasKey(x => x.InvoiceId);
        entity.Property(x => x.BillingPeriod).HasColumnType("char(7)").IsRequired();
        entity.Property(x => x.UtilityPeriod).HasColumnType("char(7)");
        entity.Property(x => x.IssuedAt).HasDefaultValueSql(UtcNowSql(sqlite));
        entity.Property(x => x.IsProrated)
            .HasComputedColumnSql(
                sqlite
                    ? "CASE WHEN [DaysCharged] <> [DaysInPeriod] THEN 1 ELSE 0 END"
                    : "CONVERT(bit, CASE WHEN [DaysCharged] <> [DaysInPeriod] THEN 1 ELSE 0 END)",
                stored: true);
        foreach (var property in new[]
                 {
                     nameof(Invoice.RentAmount), nameof(Invoice.WaterRate), nameof(Invoice.ElectricRate),
                     nameof(Invoice.TrashAmount), nameof(Invoice.AdjustmentAmount), nameof(Invoice.WaterAmount),
                     nameof(Invoice.ElectricAmount), nameof(Invoice.TotalAmount)
                 })
            entity.Property(property).HasPrecision(10, 2);
        entity.Property(x => x.WaterUnits).HasPrecision(12, 2);
        entity.Property(x => x.ElectricUnits).HasPrecision(12, 2);
        entity.Property(x => x.WaterAmount).HasComputedColumnSql("[WaterUnits] * [WaterRate]", stored: true);
        entity.Property(x => x.ElectricAmount).HasComputedColumnSql("[ElectricUnits] * [ElectricRate]", stored: true);
        entity.Property(x => x.TotalAmount).HasComputedColumnSql(
            "[RentAmount] + ([WaterUnits] * [WaterRate]) + ([ElectricUnits] * [ElectricRate]) + [TrashAmount] + [AdjustmentAmount]",
            stored: true);
        entity.Property(x => x.AdjustmentNote).HasMaxLength(200);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        entity.HasIndex(x => new { x.RoomId, x.BillingPeriod, x.TenantId }).IsUnique()
            .HasFilter("[Status] <> 'Void'");
        entity.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Tenant).WithMany(x => x.Invoices).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePayment(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<Payment>();
        entity.ToTable("Payment", table => table.HasCheckConstraint("CK_Payment_Positive", $"{Money(sqlite, "[PaidAmount]")} > 0"));
        entity.HasKey(x => x.PaymentId);
        entity.Property(x => x.PaidAmount).HasPrecision(10, 2);
        entity.Property(x => x.Method).HasMaxLength(20);
        entity.Property(x => x.SlipImageUrl).HasMaxLength(500);
        entity.Property(x => x.SlipHash).HasColumnType("char(64)");
        entity.Property(x => x.SlipRef).HasMaxLength(64);
        entity.Property(x => x.VerifiedBy).HasMaxLength(20);
        entity.Property(x => x.VerificationStatus).HasMaxLength(20).HasDefaultValue("Pending");
        entity.Property(x => x.VerificationNote).HasMaxLength(500);
        entity.Property(x => x.VoidReason).HasMaxLength(200);
        entity.Property(x => x.VoidedBy).HasMaxLength(100);
        entity.Property(x => x.RecordedAt).HasDefaultValueSql(UtcNowSql(sqlite));
        entity.HasIndex(x => x.SlipRef).IsUnique().HasFilter("[SlipRef] IS NOT NULL");
        entity.HasIndex(x => x.SlipHash).IsUnique().HasFilter("[SlipHash] IS NOT NULL");
        entity.HasOne(x => x.Invoice).WithMany(x => x.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<AuditLog>();
        entity.ToTable("AuditLog");
        entity.HasKey(x => x.AuditId);
        entity.Property(x => x.EntityName).HasMaxLength(50);
        entity.Property(x => x.EntityKey).HasMaxLength(50);
        entity.Property(x => x.FieldName).HasMaxLength(50);
        entity.Property(x => x.OldValue).HasMaxLength(100);
        entity.Property(x => x.NewValue).HasMaxLength(100);
        entity.Property(x => x.ChangedBy).HasMaxLength(100);
        entity.Property(x => x.ChangedAt).HasDefaultValueSql(UtcNowSql(sqlite));
        entity.HasIndex(x => new { x.EntityName, x.EntityKey, x.ChangedAt });
    }

    private static void ConfigureSettlement(ModelBuilder modelBuilder, bool sqlite)
    {
        var entity = modelBuilder.Entity<MoveOutSettlement>();
        entity.ToTable("MoveOutSettlement");
        entity.HasKey(x => x.SettlementId);
        entity.HasIndex(x => x.TenantId).IsUnique();
        entity.Property(x => x.SettledAt).HasDefaultValueSql(UtcNowSql(sqlite));
        foreach (var property in new[]
                 {
                     nameof(MoveOutSettlement.DepositAmount), nameof(MoveOutSettlement.FinalWaterAmount),
                     nameof(MoveOutSettlement.FinalElectricAmount), nameof(MoveOutSettlement.OutstandingAmount),
                     nameof(MoveOutSettlement.DeductionAmount), nameof(MoveOutSettlement.TotalDeducted),
                     nameof(MoveOutSettlement.RefundAmount), nameof(MoveOutSettlement.AmountDueFromTenant),
                     nameof(MoveOutSettlement.ForfeitedAmount)
                 })
            entity.Property(property).HasPrecision(10, 2);
        entity.Property(x => x.MonthsStayed).HasPrecision(5, 2);
        entity.Property(x => x.TotalDeducted).HasComputedColumnSql(
            "[FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]", stored: true);
        const string deducted = "([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount])";
        entity.Property(x => x.RefundAmount).HasComputedColumnSql(
            $"CASE WHEN [IsForfeited] = 1 THEN 0 WHEN [DepositAmount] > {deducted} THEN [DepositAmount] - {deducted} ELSE 0 END", stored: true);
        entity.Property(x => x.AmountDueFromTenant).HasComputedColumnSql(
            $"CASE WHEN {deducted} > [DepositAmount] THEN {deducted} - [DepositAmount] ELSE 0 END", stored: true);
        entity.Property(x => x.ForfeitedAmount).HasComputedColumnSql(
            $"CASE WHEN [IsForfeited] = 1 AND [DepositAmount] > {deducted} THEN [DepositAmount] - {deducted} ELSE 0 END", stored: true);
        entity.Property(x => x.ForfeitReason).HasMaxLength(200);
        entity.Property(x => x.RefundMethod).HasMaxLength(20);
        entity.Property(x => x.AmountDueCollectionMethod).HasMaxLength(20);
        entity.Property(x => x.Note).HasMaxLength(500);
        entity.HasOne(x => x.Tenant).WithOne().HasForeignKey<MoveOutSettlement>(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);

        var deduction = modelBuilder.Entity<SettlementDeduction>();
        deduction.ToTable("SettlementDeduction", table => table.HasCheckConstraint("CK_Deduction_Positive", $"{Money(sqlite, "[Amount]")} > 0"));
        deduction.HasKey(x => x.DeductionId);
        deduction.Property(x => x.Description).HasMaxLength(200);
        deduction.Property(x => x.Amount).HasPrecision(10, 2);
        deduction.Property(x => x.PhotoUrl).HasMaxLength(500);
        deduction.HasOne(x => x.Settlement).WithMany(x => x.Deductions).HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void Seed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>().HasData(
            new Room { RoomId = 1, RoomNumber = "1", MonthlyRent = 1800m, PayeeCents = .01m },
            new Room { RoomId = 2, RoomNumber = "2", MonthlyRent = 2000m, PayeeCents = .02m },
            new Room { RoomId = 3, RoomNumber = "3", MonthlyRent = 2200m, PayeeCents = .03m },
            new Room { RoomId = 4, RoomNumber = "4", MonthlyRent = 2000m, PayeeCents = .04m },
            new Room { RoomId = 5, RoomNumber = "5", MonthlyRent = 2000m, PayeeCents = .05m },
            new Room { RoomId = 6, RoomNumber = "6", MonthlyRent = 1800m, PayeeCents = .06m });
        modelBuilder.Entity<UtilityRate>().HasData(new UtilityRate
        {
            RateId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            WaterPerUnit = 20m,
            ElectricPerUnit = 12m,
            TrashPerMonth = 40m,
            Note = "อัตราเริ่มต้น"
        });
        modelBuilder.Entity<BillingPolicy>().HasData(new BillingPolicy
        {
            PolicyId = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            GraceDays = 5,
            LateFeeType = LateFeeType.None,
            LateFeeAmount = 0,
            Note = "ยังไม่เก็บค่าปรับ ใช้แค่เตือนทางไลน์"
        });
    }

    private static void ConfigureMessaging(ModelBuilder modelBuilder, bool sqlite)
    {
        var code = modelBuilder.Entity<TenantLinkCode>();
        code.ToTable("TenantLinkCode");
        code.HasKey(x => x.LinkCodeId);
        code.Property(x => x.CodeHash).HasColumnType("char(64)");
        code.Property(x => x.CreatedAt).HasDefaultValueSql(UtcNowSql(sqlite));
        code.HasIndex(x => x.CodeHash).IsUnique();
        code.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);

        var notification = modelBuilder.Entity<NotificationLog>();
        notification.ToTable("NotificationLog");
        notification.HasKey(x => x.NotificationId);
        notification.Property(x => x.NotificationType).HasMaxLength(30);
        notification.Property(x => x.ExternalMessageId).HasMaxLength(100);
        notification.Property(x => x.SentAt).HasDefaultValueSql(UtcNowSql(sqlite));
        notification.HasIndex(x => new { x.InvoiceId, x.NotificationType }).IsUnique()
            .HasFilter("[InvoiceId] IS NOT NULL");
        notification.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        notification.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
