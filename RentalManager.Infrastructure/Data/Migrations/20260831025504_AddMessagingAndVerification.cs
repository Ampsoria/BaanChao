using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerificationNote",
                table: "Payment",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "Payment",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateTable(
                name: "NotificationLog",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    NotificationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLog", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_NotificationLog_Invoice_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoice",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLog_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantLinkCode",
                columns: table => new
                {
                    LinkCodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "char(64)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLinkCode", x => x.LinkCodeId);
                    table.ForeignKey(
                        name: "FK_TenantLinkCode_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenant",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLog_InvoiceId_NotificationType",
                table: "NotificationLog",
                columns: new[] { "InvoiceId", "NotificationType" },
                unique: true,
                filter: "[InvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLog_TenantId",
                table: "NotificationLog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLinkCode_CodeHash",
                table: "TenantLinkCode",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantLinkCode_TenantId",
                table: "TenantLinkCode",
                column: "TenantId");

            migrationBuilder.Sql("""
                CREATE OR ALTER VIEW dbo.vw_InvoiceStatus AS
                SELECT i.InvoiceId, r.RoomNumber, i.BillingPeriod, i.TotalAmount,
                    ISNULL(p.PaidAmount, 0) AS PaidAmount,
                    CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                         THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) ELSE 0 END AS Outstanding,
                    i.TotalAmount + r.PayeeCents AS TransferAmount, i.DueDate,
                    CASE WHEN ISNULL(p.PaidAmount, 0) >= i.TotalAmount THEN N'ชำระแล้ว'
                         WHEN ISNULL(p.PaidAmount, 0) > 0 THEN N'ชำระบางส่วน'
                         WHEN i.DueDate < CAST(GETUTCDATE() AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time' AS DATE)
                              THEN N'เกินกำหนด' ELSE N'รอชำระ' END AS StatusText
                FROM dbo.Invoice i JOIN dbo.Room r ON r.RoomId = i.RoomId
                OUTER APPLY (SELECT SUM(PaidAmount) AS PaidAmount FROM dbo.Payment p
                             WHERE p.InvoiceId = i.InvoiceId AND p.VerificationStatus = 'Verified') p
                WHERE i.Status <> 'Void';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER VIEW dbo.vw_InvoiceStatus AS
                SELECT i.InvoiceId, r.RoomNumber, i.BillingPeriod, i.TotalAmount,
                    ISNULL(p.PaidAmount, 0) AS PaidAmount,
                    CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                         THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) ELSE 0 END AS Outstanding,
                    i.TotalAmount + r.PayeeCents AS TransferAmount, i.DueDate,
                    CASE WHEN ISNULL(p.PaidAmount, 0) >= i.TotalAmount THEN N'ชำระแล้ว'
                         WHEN ISNULL(p.PaidAmount, 0) > 0 THEN N'ชำระบางส่วน'
                         WHEN i.DueDate < CAST(GETUTCDATE() AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time' AS DATE)
                              THEN N'เกินกำหนด' ELSE N'รอชำระ' END AS StatusText
                FROM dbo.Invoice i JOIN dbo.Room r ON r.RoomId = i.RoomId
                OUTER APPLY (SELECT SUM(PaidAmount) AS PaidAmount FROM dbo.Payment p WHERE p.InvoiceId = i.InvoiceId) p
                WHERE i.Status <> 'Void';
                """);
            migrationBuilder.DropTable(
                name: "NotificationLog");

            migrationBuilder.DropTable(
                name: "TenantLinkCode");

            migrationBuilder.DropColumn(
                name: "VerificationNote",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "Payment");
        }
    }
}
