using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowInvoiceReplacementAfterVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoice_RoomId_BillingPeriod_TenantId",
                table: "Invoice");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_RoomId_BillingPeriod_TenantId",
                table: "Invoice",
                columns: new[] { "RoomId", "BillingPeriod", "TenantId" },
                unique: true,
                filter: "[Status] <> 'Void'");

            ReplaceInvoiceGenerationProcedure(migrationBuilder, includeVoid: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ReplaceInvoiceGenerationProcedure(migrationBuilder, includeVoid: true);

            migrationBuilder.DropIndex(
                name: "IX_Invoice_RoomId_BillingPeriod_TenantId",
                table: "Invoice");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_RoomId_BillingPeriod_TenantId",
                table: "Invoice",
                columns: new[] { "RoomId", "BillingPeriod", "TenantId" },
                unique: true);
        }

        private static void ReplaceInvoiceGenerationProcedure(MigrationBuilder migrationBuilder, bool includeVoid)
        {
            var statusCondition = includeVoid ? "" : " AND i.Status <> 'Void'";
            migrationBuilder.Sql($$"""
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
                            AND i.BillingPeriod = @BillingPeriod{{statusCondition}});

                    SELECT @@ROWCOUNT AS CreatedCount;
                END;
                """);
        }
    }
}
