using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "BillingPolicy",
                columns: table => new
                {
                    PolicyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    GraceDays = table.Column<byte>(type: "tinyint", nullable: false),
                    LateFeeType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LateFeeAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    LateFeeCap = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPolicy", x => x.PolicyId);
                    table.CheckConstraint("CK_LateFee_NonNegative", "[LateFeeAmount] >= 0 AND ([LateFeeCap] IS NULL OR [LateFeeCap] >= 0)");
                    table.CheckConstraint("CK_LateFeeType", "[LateFeeType] IN ('None','PerDay','Flat')");
                });

            migrationBuilder.CreateTable(
                name: "Room",
                columns: table => new
                {
                    RoomId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MonthlyRent = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PayeeCents = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Room", x => x.RoomId);
                    table.CheckConstraint("CK_Room_Rent", "[MonthlyRent] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "UtilityRate",
                columns: table => new
                {
                    RateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    WaterPerUnit = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ElectricPerUnit = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TrashPerMonth = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityRate", x => x.RateId);
                    table.CheckConstraint("CK_UtilityRate_NonNegative", "[WaterPerUnit] >= 0 AND [ElectricPerUnit] >= 0 AND [TrashPerMonth] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "MeterReading",
                columns: table => new
                {
                    ReadingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    BillingPeriod = table.Column<string>(type: "char(7)", nullable: false),
                    ReadAt = table.Column<DateOnly>(type: "date", nullable: false),
                    WaterPrev = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    WaterCurrent = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ElectricPrev = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ElectricCurrent = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    WaterUnits = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false, computedColumnSql: "[WaterCurrent] - [WaterPrev]", stored: true),
                    ElectricUnits = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false, computedColumnSql: "[ElectricCurrent] - [ElectricPrev]", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterReading", x => x.ReadingId);
                    table.CheckConstraint("CK_Electric_NotNegative", "[ElectricCurrent] >= [ElectricPrev]");
                    table.CheckConstraint("CK_Water_NotNegative", "[WaterCurrent] >= [WaterPrev]");
                    table.ForeignKey(
                        name: "FK_MeterReading_Room_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Room",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tenant",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LineUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MovedInAt = table.Column<DateOnly>(type: "date", nullable: false),
                    MovedOutAt = table.Column<DateOnly>(type: "date", nullable: true),
                    DepositAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DepositReceivedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    DepositRefundedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    MinimumStayMonths = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenant", x => x.TenantId);
                    table.CheckConstraint("CK_Tenant_Deposit", "[DepositAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_Tenant_Room_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Room",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoice",
                columns: table => new
                {
                    InvoiceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    BillingPeriod = table.Column<string>(type: "char(7)", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    DaysCharged = table.Column<short>(type: "smallint", nullable: false),
                    DaysInPeriod = table.Column<byte>(type: "tinyint", nullable: false),
                    IsProrated = table.Column<bool>(type: "bit", nullable: false, computedColumnSql: "CONVERT(bit, CASE WHEN [DaysCharged] <> [DaysInPeriod] THEN 1 ELSE 0 END)", stored: true),
                    RentAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    WaterUnits = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    WaterRate = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ElectricUnits = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ElectricRate = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TrashAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    AdjustmentNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WaterAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "[WaterUnits] * [WaterRate]", stored: true),
                    ElectricAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "[ElectricUnits] * [ElectricRate]", stored: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "[RentAmount] + ([WaterUnits] * [WaterRate]) + ([ElectricUnits] * [ElectricRate]) + [TrashAmount] + [AdjustmentAmount]", stored: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoice", x => x.InvoiceId);
                    table.CheckConstraint("CK_Invoice_Days", "[DaysCharged] > 0 AND [DaysCharged] <= [DaysInPeriod]");
                    table.CheckConstraint("CK_Invoice_Units", "[WaterUnits] >= 0 AND [ElectricUnits] >= 0");
                    table.ForeignKey(
                        name: "FK_Invoice_Room_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Room",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoice_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MoveOutSettlement",
                columns: table => new
                {
                    SettlementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    MoveOutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DepositAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    FinalWaterAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    FinalElectricAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DeductionAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    IsForfeited = table.Column<bool>(type: "bit", nullable: false),
                    ForfeitReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MonthsStayed = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TotalDeducted = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "[FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]", stored: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "CASE WHEN [IsForfeited] = 1 THEN 0 WHEN [DepositAmount] > ([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]) THEN [DepositAmount] - ([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]) ELSE 0 END", stored: true),
                    AmountDueFromTenant = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "CASE WHEN ([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]) > [DepositAmount] THEN ([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]) - [DepositAmount] ELSE 0 END", stored: true),
                    ForfeitedAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false, computedColumnSql: "CASE WHEN [IsForfeited] = 1 AND [DepositAmount] > ([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]) THEN [DepositAmount] - ([FinalWaterAmount] + [FinalElectricAmount] + [OutstandingAmount] + [DeductionAmount]) ELSE 0 END", stored: true),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoveOutSettlement", x => x.SettlementId);
                    table.ForeignKey(
                        name: "FK_MoveOutSettlement_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payment",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SlipImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SlipHash = table.Column<string>(type: "char(64)", nullable: true),
                    SlipRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payment", x => x.PaymentId);
                    table.CheckConstraint("CK_Payment_Positive", "[PaidAmount] > 0");
                    table.ForeignKey(
                        name: "FK_Payment_Invoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoice",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementDeduction",
                columns: table => new
                {
                    DeductionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettlementId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PhotoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementDeduction", x => x.DeductionId);
                    table.CheckConstraint("CK_Deduction_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SettlementDeduction_MoveOutSettlement_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "MoveOutSettlement",
                        principalColumn: "SettlementId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "BillingPolicy",
                columns: new[] { "PolicyId", "EffectiveFrom", "GraceDays", "LateFeeAmount", "LateFeeCap", "LateFeeType", "Note" },
                values: new object[] { 1, new DateOnly(2026, 1, 1), (byte)5, 0m, null, "None", "ยังไม่เก็บค่าปรับ ใช้แค่เตือนทางไลน์" });

            migrationBuilder.InsertData(
                table: "Room",
                columns: new[] { "RoomId", "IsActive", "MonthlyRent", "PayeeCents", "RoomNumber" },
                values: new object[,]
                {
                    { 1, true, 1800m, 0.01m, "1" },
                    { 2, true, 2000m, 0.02m, "2" },
                    { 3, true, 2200m, 0.03m, "3" },
                    { 4, true, 2000m, 0.04m, "4" },
                    { 5, true, 2000m, 0.05m, "5" },
                    { 6, true, 1800m, 0.06m, "6" }
                });

            migrationBuilder.InsertData(
                table: "UtilityRate",
                columns: new[] { "RateId", "EffectiveFrom", "ElectricPerUnit", "Note", "TrashPerMonth", "WaterPerUnit" },
                values: new object[] { 1, new DateOnly(2026, 1, 1), 12m, "อัตราเริ่มต้น", 40m, 20m });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityName_EntityKey_ChangedAt",
                table: "AuditLog",
                columns: new[] { "EntityName", "EntityKey", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPolicy_EffectiveFrom",
                table: "BillingPolicy",
                column: "EffectiveFrom",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_RoomId_BillingPeriod_TenantId",
                table: "Invoice",
                columns: new[] { "RoomId", "BillingPeriod", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_TenantId",
                table: "Invoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MeterReading_RoomId_BillingPeriod",
                table: "MeterReading",
                columns: new[] { "RoomId", "BillingPeriod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoveOutSettlement_TenantId",
                table: "MoveOutSettlement",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payment_InvoiceId",
                table: "Payment",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_SlipHash",
                table: "Payment",
                column: "SlipHash",
                unique: true,
                filter: "[SlipHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_SlipRef",
                table: "Payment",
                column: "SlipRef",
                unique: true,
                filter: "[SlipRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Room_PayeeCents",
                table: "Room",
                column: "PayeeCents",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Room_RoomNumber",
                table: "Room",
                column: "RoomNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementDeduction_SettlementId",
                table: "SettlementDeduction",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenant_LineUserId",
                table: "Tenant",
                column: "LineUserId",
                filter: "[LineUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tenant_RoomId",
                table: "Tenant",
                column: "RoomId",
                unique: true,
                filter: "[MovedOutAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityRate_EffectiveFrom",
                table: "UtilityRate",
                column: "EffectiveFrom",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "BillingPolicy");

            migrationBuilder.DropTable(
                name: "MeterReading");

            migrationBuilder.DropTable(
                name: "Payment");

            migrationBuilder.DropTable(
                name: "SettlementDeduction");

            migrationBuilder.DropTable(
                name: "UtilityRate");

            migrationBuilder.DropTable(
                name: "Invoice");

            migrationBuilder.DropTable(
                name: "MoveOutSettlement");

            migrationBuilder.DropTable(
                name: "Tenant");

            migrationBuilder.DropTable(
                name: "Room");
        }
    }
}
