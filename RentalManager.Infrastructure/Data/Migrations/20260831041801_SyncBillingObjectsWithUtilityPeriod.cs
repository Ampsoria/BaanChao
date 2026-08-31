using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <summary>
    /// ปรับ view และ stored procedure ให้ตรงกับกฎใน CLAUDE.md ข้อ 4:
    /// ค่าน้ำ-ค่าไฟบนบิลเป็นของเดือนก่อนหน้าเสมอ (UtilityPeriod) และยอดค้างนับเฉพาะเงินที่ยืนยันแล้ว
    /// </summary>
    public partial class SyncBillingObjectsWithUtilityPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ยอดที่ถือว่า "ชำระแล้ว" ต้องนับเฉพาะ Payment ที่ VerificationStatus = 'Verified'
            // ให้ตรงกับที่ฝั่ง C# คำนวณ ไม่งั้นสลิปที่ยังรอตรวจจะทำให้ยอดค้างเพี้ยน
            migrationBuilder.Sql("""
                CREATE OR ALTER VIEW dbo.vw_InvoiceStatus AS
                SELECT
                    i.InvoiceId,
                    r.RoomNumber,
                    i.BillingPeriod,
                    i.UtilityPeriod,
                    i.TotalAmount,
                    ISNULL(p.PaidAmount, 0) AS PaidAmount,
                    CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                         THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) ELSE 0 END AS Outstanding,
                    CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                         THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) + r.PayeeCents ELSE 0 END AS TransferAmount,
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
                    SELECT SUM(p.PaidAmount) AS PaidAmount
                    FROM dbo.Payment p
                    WHERE p.InvoiceId = i.InvoiceId AND p.VerificationStatus = 'Verified'
                ) p
                WHERE i.Status <> 'Void';
                """);

            // เดินจดมิเตอร์สิ้นเดือนแล้วออกบิลวันที่ 1 → บิลงวด M ใช้เลขมิเตอร์งวด M-1
            // LEFT JOIN เพราะห้องที่ยังไม่มีเลขมิเตอร์ของงวดก่อนต้องออกบิลค่าเช่าได้ตามปกติ
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.sp_GenerateMonthlyInvoices
                    @BillingPeriod CHAR(7)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;
                    DECLARE @PeriodStart DATE = TRY_CONVERT(DATE, @BillingPeriod + '-01', 126);
                    IF @PeriodStart IS NULL THROW 50001, 'งวดบิลต้องอยู่ในรูปแบบ YYYY-MM', 1;
                    DECLARE @PeriodEnd DATE = EOMONTH(@PeriodStart);
                    DECLARE @DaysInPeriod TINYINT = DAY(@PeriodEnd);
                    DECLARE @UtilityPeriod CHAR(7) = CONVERT(CHAR(7), DATEADD(MONTH, -1, @PeriodStart), 126);
                    DECLARE @GraceDays TINYINT;
                    SELECT TOP 1 @GraceDays = GraceDays FROM dbo.BillingPolicy
                    WHERE EffectiveFrom <= @PeriodStart ORDER BY EffectiveFrom DESC;
                    IF @GraceDays IS NULL THROW 50002, 'ไม่พบนโยบายการวางบิล', 1;
                    DECLARE @DueDate DATE = DATEADD(DAY,
                        CASE WHEN @GraceDays > @DaysInPeriod THEN @DaysInPeriod ELSE @GraceDays END - 1, @PeriodStart);
                    DECLARE @Water DECIMAL(10,2), @Electric DECIMAL(10,2), @Trash DECIMAL(10,2);
                    SELECT TOP 1 @Water = WaterPerUnit, @Electric = ElectricPerUnit, @Trash = TrashPerMonth
                    FROM dbo.UtilityRate WHERE EffectiveFrom <= @PeriodStart ORDER BY EffectiveFrom DESC;
                    IF @Water IS NULL THROW 50003, 'ไม่พบอัตราค่าบริการที่มีผลในงวดนี้', 1;

                    INSERT INTO dbo.Invoice (
                        RoomId, TenantId, BillingPeriod, UtilityPeriod, DueDate, PeriodStart, PeriodEnd,
                        DaysCharged, DaysInPeriod, RentAmount, WaterUnits, WaterRate,
                        ElectricUnits, ElectricRate, TrashAmount, AdjustmentAmount, Status)
                    SELECT
                        r.RoomId, t.TenantId, @BillingPeriod,
                        CASE WHEN m.ReadingId IS NULL THEN NULL ELSE @UtilityPeriod END,
                        @DueDate, c.ChargeStart, @PeriodEnd,
                        c.DaysCharged, @DaysInPeriod,
                        FLOOR(r.MonthlyRent * c.DaysCharged / @DaysInPeriod),
                        ISNULL(m.WaterCurrent - m.WaterPrev, 0), @Water,
                        ISNULL(m.ElectricCurrent - m.ElectricPrev, 0), @Electric,
                        CASE WHEN c.DaysCharged = @DaysInPeriod THEN @Trash ELSE 0 END,
                        0, 'Unpaid'
                    FROM dbo.Room r
                    JOIN dbo.Tenant t ON t.RoomId = r.RoomId
                    -- ผู้เช่าที่เพิ่งย้ายเข้าเดือนนี้ต้องไม่โดนค่าน้ำ-ค่าไฟของผู้เช่าคนก่อน
                    LEFT JOIN dbo.MeterReading m
                        ON m.RoomId = r.RoomId
                       AND m.BillingPeriod = @UtilityPeriod
                       AND t.MovedInAt < @PeriodStart
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

            // บิลใบแรกตอนย้ายเข้าไม่มีค่าน้ำ-ค่าไฟ → UtilityPeriod เป็น NULL เสมอ
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
                        INSERT dbo.Invoice (RoomId, TenantId, BillingPeriod, UtilityPeriod, DueDate, PeriodStart, PeriodEnd,
                            DaysCharged, DaysInPeriod, RentAmount, WaterUnits, WaterRate, ElectricUnits,
                            ElectricRate, TrashAmount, AdjustmentAmount, Status)
                        VALUES (@RoomId, @TenantId, @BillingPeriod, NULL, @MovedInAt, @MovedInAt, @PeriodEnd,
                            @DaysCharged, @DaysInPeriod, FLOOR(@Rent * @DaysCharged / @DaysInPeriod),
                            0, @WaterRate, 0, @ElectricRate, 0, 0, 'Unpaid');
                    COMMIT TRANSACTION;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ย้อนกลับไปเป็นนิยามเดิมที่ใช้เลขมิเตอร์งวดเดียวกับค่าเช่า
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
        }
    }
}
