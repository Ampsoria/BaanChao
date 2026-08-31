using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingDatabaseObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER VIEW dbo.vw_InvoiceStatus AS
                SELECT
                    i.InvoiceId,
                    r.RoomNumber,
                    i.BillingPeriod,
                    i.TotalAmount,
                    ISNULL(p.PaidAmount, 0) AS PaidAmount,
                    CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                         THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) ELSE 0 END AS Outstanding,
                    i.TotalAmount + r.PayeeCents AS TransferAmount,
                    i.DueDate,
                    CASE
                        WHEN ISNULL(p.PaidAmount, 0) >= i.TotalAmount THEN N'ชำระแล้ว'
                        WHEN ISNULL(p.PaidAmount, 0) > 0 THEN N'ชำระบางส่วน'
                        WHEN i.DueDate < CAST(GETUTCDATE() AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time' AS DATE)
                            THEN N'เกินกำหนด'
                        ELSE N'รอชำระ'
                    END AS StatusText
                FROM dbo.Invoice i
                JOIN dbo.Room r ON r.RoomId = i.RoomId
                OUTER APPLY (
                    SELECT SUM(PaidAmount) AS PaidAmount
                    FROM dbo.Payment p
                    WHERE p.InvoiceId = i.InvoiceId
                ) p
                WHERE i.Status <> 'Void';
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.sp_GenerateMonthlyInvoices
                    @BillingPeriod CHAR(7)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;
                    DECLARE @PeriodStart DATE = TRY_CONVERT(DATE, @BillingPeriod + '-01');
                    IF @PeriodStart IS NULL THROW 50001, 'งวดบิลต้องอยู่ในรูปแบบ YYYY-MM', 1;
                    DECLARE @PeriodEnd DATE = EOMONTH(@PeriodStart);
                    DECLARE @DaysInPeriod TINYINT = DAY(@PeriodEnd);
                    DECLARE @GraceDays TINYINT;
                    SELECT TOP 1 @GraceDays = GraceDays FROM dbo.BillingPolicy
                    WHERE EffectiveFrom <= @PeriodStart ORDER BY EffectiveFrom DESC;
                    IF @GraceDays IS NULL THROW 50002, 'ไม่พบนโยบายการวางบิล', 1;
                    DECLARE @DueDate DATE = DATEADD(DAY, @GraceDays - 1, @PeriodStart);
                    DECLARE @Water DECIMAL(10,2), @Electric DECIMAL(10,2), @Trash DECIMAL(10,2);
                    SELECT TOP 1 @Water = WaterPerUnit, @Electric = ElectricPerUnit, @Trash = TrashPerMonth
                    FROM dbo.UtilityRate WHERE EffectiveFrom <= @PeriodStart ORDER BY EffectiveFrom DESC;
                    IF @Water IS NULL THROW 50003, 'ไม่พบอัตราค่าบริการที่มีผลในงวดนี้', 1;

                    INSERT INTO dbo.Invoice (
                        RoomId, TenantId, BillingPeriod, DueDate, PeriodStart, PeriodEnd,
                        DaysCharged, DaysInPeriod, RentAmount, WaterUnits, WaterRate,
                        ElectricUnits, ElectricRate, TrashAmount, AdjustmentAmount, Status)
                    SELECT
                        r.RoomId, t.TenantId, @BillingPeriod, @DueDate, c.ChargeStart, @PeriodEnd,
                        c.DaysCharged, @DaysInPeriod,
                        FLOOR(r.MonthlyRent * c.DaysCharged / @DaysInPeriod),
                        m.WaterCurrent - m.WaterPrev, @Water,
                        m.ElectricCurrent - m.ElectricPrev, @Electric,
                        CASE WHEN c.DaysCharged = @DaysInPeriod THEN @Trash ELSE 0 END,
                        0, 'Unpaid'
                    FROM dbo.Room r
                    JOIN dbo.Tenant t ON t.RoomId = r.RoomId
                    JOIN dbo.MeterReading m ON m.RoomId = r.RoomId AND m.BillingPeriod = @BillingPeriod
                    CROSS APPLY (SELECT CASE WHEN t.MovedInAt > @PeriodStart THEN t.MovedInAt ELSE @PeriodStart END) s(ChargeStart)
                    CROSS APPLY (SELECT s.ChargeStart, DATEDIFF(DAY, s.ChargeStart, @PeriodEnd) + 1) c(ChargeStart, DaysCharged)
                    WHERE r.IsActive = 1
                      AND t.MovedInAt <= @PeriodEnd
                      AND (t.MovedOutAt IS NULL OR t.MovedOutAt >= @PeriodStart)
                      AND c.DaysCharged > 0
                      AND NOT EXISTS (
                          SELECT 1 FROM dbo.Invoice i
                          WHERE i.RoomId = r.RoomId AND i.TenantId = t.TenantId
                            AND i.BillingPeriod = @BillingPeriod);

                    SELECT @@ROWCOUNT AS CreatedCount;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.sp_CreateMoveInInvoice
                    @TenantId INT,
                    @WaterReading DECIMAL(12,2),
                    @ElectricReading DECIMAL(12,2)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;
                    BEGIN TRANSACTION;
                    DECLARE @RoomId INT, @MovedInAt DATE, @Rent DECIMAL(10,2);
                    SELECT @RoomId = t.RoomId, @MovedInAt = t.MovedInAt, @Rent = r.MonthlyRent
                    FROM dbo.Tenant t JOIN dbo.Room r ON r.RoomId = t.RoomId WHERE t.TenantId = @TenantId;
                    IF @RoomId IS NULL THROW 50004, 'ไม่พบผู้เช่า', 1;
                    DECLARE @BillingPeriod CHAR(7) = CONVERT(CHAR(7), @MovedInAt, 126);
                    DECLARE @PeriodEnd DATE = EOMONTH(@MovedInAt);
                    DECLARE @DaysInPeriod TINYINT = DAY(@PeriodEnd);
                    DECLARE @DaysCharged SMALLINT = DATEDIFF(DAY, @MovedInAt, @PeriodEnd) + 1;
                    DECLARE @WaterRate DECIMAL(10,2), @ElectricRate DECIMAL(10,2);
                    SELECT TOP 1 @WaterRate = WaterPerUnit, @ElectricRate = ElectricPerUnit
                    FROM dbo.UtilityRate WHERE EffectiveFrom <= @MovedInAt ORDER BY EffectiveFrom DESC;
                    IF @WaterRate IS NULL THROW 50005, 'ไม่พบอัตราค่าบริการ', 1;

                    IF NOT EXISTS (SELECT 1 FROM dbo.MeterReading WHERE RoomId = @RoomId AND BillingPeriod = @BillingPeriod)
                        INSERT dbo.MeterReading (RoomId, BillingPeriod, ReadAt, WaterPrev, WaterCurrent, ElectricPrev, ElectricCurrent)
                        VALUES (@RoomId, @BillingPeriod, @MovedInAt, @WaterReading, @WaterReading, @ElectricReading, @ElectricReading);

                    IF NOT EXISTS (SELECT 1 FROM dbo.Invoice WHERE RoomId = @RoomId AND TenantId = @TenantId AND BillingPeriod = @BillingPeriod)
                        INSERT dbo.Invoice (RoomId, TenantId, BillingPeriod, DueDate, PeriodStart, PeriodEnd,
                            DaysCharged, DaysInPeriod, RentAmount, WaterUnits, WaterRate, ElectricUnits,
                            ElectricRate, TrashAmount, AdjustmentAmount, Status)
                        VALUES (@RoomId, @TenantId, @BillingPeriod, @MovedInAt, @MovedInAt, @PeriodEnd,
                            @DaysCharged, @DaysInPeriod, FLOOR(@Rent * @DaysCharged / @DaysInPeriod),
                            0, @WaterRate, 0, @ElectricRate, 0, 0, 'Unpaid');
                    COMMIT TRANSACTION;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.sp_CreateMoveOutSettlement
                    @TenantId INT,
                    @MoveOutDate DATE,
                    @WaterFinal DECIMAL(12,2),
                    @ElectricFinal DECIMAL(12,2)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;
                    DECLARE @MovedIn DATE, @Deposit DECIMAL(10,2), @MinimumStay TINYINT, @RoomId INT;
                    SELECT @MovedIn = MovedInAt, @Deposit = DepositAmount,
                           @MinimumStay = MinimumStayMonths, @RoomId = RoomId
                    FROM dbo.Tenant WHERE TenantId = @TenantId AND MovedOutAt IS NULL;
                    IF @MovedIn IS NULL THROW 50006, 'ไม่พบผู้เช่าที่ยังพักอยู่', 1;
                    DECLARE @MonthsStayed DECIMAL(5,2) = DATEDIFF(DAY, @MovedIn, @MoveOutDate) / 30.44;
                    DECLARE @WaterRate DECIMAL(10,2), @ElectricRate DECIMAL(10,2);
                    SELECT TOP 1 @WaterRate = WaterPerUnit, @ElectricRate = ElectricPerUnit
                    FROM dbo.UtilityRate WHERE EffectiveFrom <= @MoveOutDate ORDER BY EffectiveFrom DESC;
                    DECLARE @WaterPrev DECIMAL(12,2), @ElectricPrev DECIMAL(12,2);
                    SELECT TOP 1 @WaterPrev = WaterCurrent, @ElectricPrev = ElectricCurrent
                    FROM dbo.MeterReading WHERE RoomId = @RoomId AND ReadAt <= @MoveOutDate
                    ORDER BY ReadAt DESC, ReadingId DESC;
                    IF @WaterFinal < @WaterPrev OR @ElectricFinal < @ElectricPrev
                        THROW 50007, 'เลขมิเตอร์วันย้ายออกน้อยกว่าครั้งก่อน', 1;
                    DECLARE @Outstanding DECIMAL(10,2);
                    SELECT @Outstanding = ISNULL(SUM(Outstanding), 0)
                    FROM dbo.vw_InvoiceStatus v
                    JOIN dbo.Invoice i ON i.InvoiceId = v.InvoiceId
                    WHERE i.TenantId = @TenantId AND v.Outstanding > 0;

                    INSERT dbo.MoveOutSettlement (TenantId, MoveOutDate, DepositAmount,
                        FinalWaterAmount, FinalElectricAmount, OutstandingAmount, DeductionAmount,
                        IsForfeited, ForfeitReason, MonthsStayed)
                    VALUES (@TenantId, @MoveOutDate, @Deposit,
                        (@WaterFinal - @WaterPrev) * @WaterRate,
                        (@ElectricFinal - @ElectricPrev) * @ElectricRate,
                        @Outstanding, 0,
                        CASE WHEN @MonthsStayed < (@MinimumStay - 0.5) THEN 1 ELSE 0 END,
                        CASE WHEN @MonthsStayed < (@MinimumStay - 0.5)
                             THEN N'อยู่ไม่ครบ ' + CAST(@MinimumStay AS NVARCHAR(3)) + N' เดือน' ELSE NULL END,
                        @MonthsStayed);
                    UPDATE dbo.Tenant SET MovedOutAt = @MoveOutDate WHERE TenantId = @TenantId;
                    SELECT SCOPE_IDENTITY() AS SettlementId;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_CreateMoveOutSettlement;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_CreateMoveInInvoice;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_GenerateMonthlyInvoices;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_InvoiceStatus;");
        }
    }
}
