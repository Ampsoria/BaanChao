using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RentalManager.Infrastructure.Data;

#nullable disable

namespace RentalManager.Infrastructure.Data.Migrations;

[DbContext(typeof(RentalDbContext))]
[Migration("20260831040000_FixOutstandingTransferAmountView")]
public sealed class FixOutstandingTransferAmountView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR ALTER VIEW dbo.vw_InvoiceStatus AS
        SELECT i.InvoiceId, r.RoomNumber, i.BillingPeriod, i.TotalAmount,
            ISNULL(p.PaidAmount, 0) AS PaidAmount,
            CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                 THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) ELSE 0 END AS Outstanding,
            CASE WHEN i.TotalAmount > ISNULL(p.PaidAmount, 0)
                 THEN i.TotalAmount - ISNULL(p.PaidAmount, 0) + r.PayeeCents ELSE 0 END AS TransferAmount,
            i.DueDate,
            CASE WHEN ISNULL(p.PaidAmount, 0) >= i.TotalAmount THEN N'ชำระแล้ว'
                 WHEN ISNULL(p.PaidAmount, 0) > 0 THEN N'ชำระบางส่วน'
                 WHEN i.DueDate < CAST(GETUTCDATE() AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time' AS DATE)
                      THEN N'เกินกำหนด' ELSE N'รอชำระ' END AS StatusText
        FROM dbo.Invoice i JOIN dbo.Room r ON r.RoomId = i.RoomId
        OUTER APPLY (SELECT SUM(PaidAmount) AS PaidAmount FROM dbo.Payment p
                     WHERE p.InvoiceId = i.InvoiceId AND p.VerificationStatus = 'Verified') p
        WHERE i.Status <> 'Void';
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
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
