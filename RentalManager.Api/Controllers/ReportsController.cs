using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Api.Controllers;

[Route("api/admin/reports")]
public sealed class ReportsController(RentalDbContext db) : AdminControllerBase
{
    [HttpGet("invoices.csv")]
    public async Task<IActionResult> Invoices(string? billingPeriod, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(billingPeriod))
            query = query.Where(x => x.BillingPeriod == billingPeriod);
        var rows = await query.OrderBy(x => x.BillingPeriod).ThenBy(x => x.Room.RoomNumber).Select(x => new
        {
            x.InvoiceId,
            x.Room.RoomNumber,
            x.Tenant.FullName,
            x.BillingPeriod,
            x.UtilityPeriod,
            x.DueDate,
            x.Status,
            x.RentAmount,
            x.WaterUnits,
            x.WaterAmount,
            x.ElectricUnits,
            x.ElectricAmount,
            x.TrashAmount,
            x.AdjustmentAmount,
            x.TotalAmount,
            Paid = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount)
        }).ToListAsync(ct);
        return CsvFile("invoices.csv",
            ["InvoiceId", "Room", "Tenant", "BillingPeriod", "UtilityPeriod", "DueDate", "Status", "Rent", "WaterUnits", "WaterAmount", "ElectricUnits", "ElectricAmount", "Trash", "Adjustment", "Total", "Paid", "Outstanding"],
            rows.Select(x => new object?[]
            {
                x.InvoiceId, x.RoomNumber, x.FullName, x.BillingPeriod, x.UtilityPeriod, x.DueDate, x.Status,
                x.RentAmount, x.WaterUnits, x.WaterAmount, x.ElectricUnits, x.ElectricAmount,
                x.TrashAmount, x.AdjustmentAmount, x.TotalAmount, x.Paid,
                x.Status == InvoiceStatus.Void ? 0 : Math.Max(x.TotalAmount - x.Paid, 0)
            }));
    }

    [HttpGet("payments.csv")]
    public async Task<IActionResult> Payments(CancellationToken ct)
    {
        var rows = await db.Payments.AsNoTracking().OrderBy(x => x.PaidAt).Select(x => new object?[]
        {
            x.PaymentId, x.InvoiceId, x.Invoice.Room.RoomNumber, x.Invoice.Tenant.FullName,
            x.Invoice.BillingPeriod, x.PaidAmount, x.PaidAt, x.Method, x.VerificationStatus,
            x.VerifiedBy, x.SlipRef, x.VoidedAt, x.VoidReason, x.VoidedBy
        }).ToListAsync(ct);
        return CsvFile("payments.csv",
            ["PaymentId", "InvoiceId", "Room", "Tenant", "BillingPeriod", "PaidAmount", "PaidAtUtc", "Method", "Status", "VerifiedBy", "SlipRef", "VoidedAtUtc", "VoidReason", "VoidedBy"], rows);
    }

    [HttpGet("meters.csv")]
    public async Task<IActionResult> Meters(CancellationToken ct)
    {
        var rows = await db.MeterReadings.AsNoTracking().OrderBy(x => x.BillingPeriod)
            .ThenBy(x => x.Room.RoomNumber).Select(x => new object?[]
            {
                x.ReadingId, x.Room.RoomNumber, x.BillingPeriod, x.ReadAt,
                x.WaterPrev, x.WaterCurrent, x.WaterUnits,
                x.ElectricPrev, x.ElectricCurrent, x.ElectricUnits
            }).ToListAsync(ct);
        return CsvFile("meters.csv",
            ["ReadingId", "Room", "BillingPeriod", "ReadAt", "WaterPrevious", "WaterCurrent", "WaterUnits", "ElectricPrevious", "ElectricCurrent", "ElectricUnits"], rows);
    }

    [HttpGet("meter-checkpoints.csv")]
    public async Task<IActionResult> MeterCheckpoints(CancellationToken ct)
    {
        var rows = await db.MeterCheckpoints.AsNoTracking().OrderBy(x => x.RecordedAt)
            .ThenBy(x => x.Room.RoomNumber).Select(x => new object?[]
            {
                x.MeterCheckpointId, x.Room.RoomNumber, x.Tenant.FullName, x.RecordedAt,
                x.Kind, x.WaterReading, x.ElectricReading
            }).ToListAsync(ct);
        return CsvFile("meter-checkpoints.csv",
            ["CheckpointId", "Room", "Tenant", "RecordedAt", "Kind", "WaterReading", "ElectricReading"], rows);
    }

    [HttpGet("tenants.csv")]
    public async Task<IActionResult> Tenants(CancellationToken ct)
    {
        var rows = await db.Tenants.AsNoTracking().OrderBy(x => x.Room.RoomNumber).ThenBy(x => x.MovedInAt)
            .Select(x => new object?[]
            {
                x.TenantId, x.Room.RoomNumber, x.FullName, x.Phone, x.MovedInAt, x.MovedOutAt,
                x.DepositAmount, x.DepositReceivedAt, x.DepositRefundedAt, x.MinimumStayMonths,
                x.PreferredChannel, x.LineUserId != null ? "Linked" : "NotLinked"
            }).ToListAsync(ct);
        return CsvFile("tenants.csv",
            ["TenantId", "Room", "FullName", "Phone", "MovedInAt", "MovedOutAt", "Deposit", "DepositReceivedAt", "DepositRefundedAt", "MinimumStayMonths", "PreferredChannel", "LineStatus"], rows);
    }

    [HttpGet("settlements.csv")]
    public async Task<IActionResult> Settlements(CancellationToken ct)
    {
        var rows = await db.MoveOutSettlements.AsNoTracking().OrderBy(x => x.MoveOutDate).Select(x => new object?[]
        {
            x.SettlementId, x.Tenant.Room.RoomNumber, x.Tenant.FullName, x.MoveOutDate,
            x.DepositAmount, x.TotalDeducted, x.RefundAmount, x.RefundedAt, x.RefundMethod,
            x.AmountDueFromTenant, x.AmountDueCollectedAt, x.AmountDueCollectionMethod,
            x.IsForfeited, x.ForfeitedAmount
        }).ToListAsync(ct);
        return CsvFile("settlements.csv",
            ["SettlementId", "Room", "Tenant", "MoveOutDate", "Deposit", "TotalDeducted", "RefundAmount", "RefundedAtUtc", "RefundMethod", "AmountDue", "AmountDueCollectedAtUtc", "CollectionMethod", "IsForfeited", "ForfeitedAmount"], rows);
    }

    private FileContentResult CsvFile(string filename, IReadOnlyCollection<string> headers, IEnumerable<object?[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(value => CsvCell(value, protectFormula: false))));
        foreach (var row in rows)
            builder.AppendLine(string.Join(',', row.Select(value => CsvCell(value, protectFormula: value is string))));
        var content = Encoding.UTF8.GetBytes(builder.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);
        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.00", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static string CsvCell(object? rawValue, bool protectFormula)
    {
        var value = Format(rawValue);
        // ป้องกัน Excel ตีค่าข้อมูลผู้ใช้เป็นสูตรเมื่อเปิด CSV
        if (protectFormula && value.Length > 0 && value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
