using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;
using RentalManager.Core.Interfaces;
using RentalManager.Api.Services;

namespace RentalManager.Api.Controllers;

[Route("api/admin/invoices")]
public sealed class InvoicesController(
    RentalDbContext db, RentalOperationsService service, ILineMessenger line,
    PublicLinkSigner signer, IConfiguration configuration) : AdminControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetInvoices(string? billingPeriod, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking()
            .Include(x => x.Room).Include(x => x.Tenant).Include(x => x.Payments).AsQueryable();
        if (!string.IsNullOrWhiteSpace(billingPeriod))
            query = query.Where(x => x.BillingPeriod == billingPeriod);
        var rows = await query.OrderByDescending(x => x.BillingPeriod).ThenBy(x => x.Room.RoomNumber).Select(x => new
        {
            x.InvoiceId,
            x.TenantId,
            x.Room.RoomNumber,
            x.Tenant.FullName,
            x.Tenant.LineUserId,
            x.Tenant.PreferredChannel,
            x.BillingPeriod,
            x.UtilityPeriod,
            x.DueDate,
            x.RentAmount,
            x.WaterAmount,
            x.ElectricAmount,
            x.TrashAmount,
            x.AdjustmentAmount,
            x.TotalAmount,
            PaidAmount = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount),
            Outstanding = x.Status == InvoiceStatus.Void
                ? 0
                : Math.Max(x.TotalAmount - x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount), 0),
            TransferAmount = x.Status != InvoiceStatus.Void &&
                             Math.Max(x.TotalAmount - x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount), 0) > 0
                ? Math.Max(x.TotalAmount - x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount), 0) + x.Room.PayeeCents
                : 0,
            x.Status,
            CanVoid = x.Status != InvoiceStatus.Void &&
                      !x.Payments.Any(p => p.VerificationStatus == "Verified"),
            Payments = x.Payments.OrderByDescending(p => p.PaidAt).Select(p => new
            {
                p.PaymentId,
                p.PaidAmount,
                p.PaidAt,
                p.VerificationStatus,
                p.VerifiedBy,
                p.VerificationNote,
                HasSlip = p.SlipImageUrl != null
            })
        }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateInvoices(GenerateInvoicesRequest request, CancellationToken ct) =>
        Ok(await service.GenerateMonthlyInvoicesAsync(request.BillingPeriod, UserName, ct));

    [HttpPost("{invoiceId:int}/void")]
    public async Task<IActionResult> VoidInvoice(int invoiceId, VoidInvoiceRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is < 3 or > 200)
            return BadRequest(new { message = "กรุณาระบุเหตุผล 3–200 ตัวอักษร" });
        var invoice = await db.Invoices.Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.InvoiceId == invoiceId, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == InvoiceStatus.Void)
            return Ok(new { message = "บิลนี้ถูกยกเลิกอยู่แล้ว" });
        if (invoice.Payments.Any(x => x.VerificationStatus == "Verified"))
            return Conflict(new { message = "ยกเลิกบิลที่มีการชำระเงินยืนยันแล้วไม่ได้ กรุณาตรวจสอบยอดเงินก่อน" });
        if (await db.MoveOutSettlements.AnyAsync(x => x.TenantId == invoice.TenantId, ct))
            return Conflict(new { message = "ยกเลิกบิลไม่ได้ เพราะยอดถูกนำไปสรุปการย้ายออกแล้ว" });

        var wasSent = await db.NotificationLogs.AnyAsync(
            x => x.InvoiceId == invoiceId && x.NotificationType == "Invoice", ct);
        invoice.Status = InvoiceStatus.Void;
        db.AuditLogs.Add(Audit("Invoice", invoiceId.ToString(), "Void",
            $"{invoice.BillingPeriod}|{invoice.TotalAmount:N2}", reason.Length <= 100 ? reason : reason[..100]));
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            message = wasSent
                ? "ยกเลิกบิลแล้ว บิลนี้เคยส่งให้ผู้เช่า กรุณาแจ้งผู้เช่าด้วย"
                : "ยกเลิกบิลแล้ว สามารถแก้ข้อมูลและออกบิลงวดเดิมใหม่ได้"
        });
    }

    /// <summary>บิล PDF สำหรับผู้เช่าที่รับบิลเป็นกระดาษ — ระบบต้องใช้งานได้ครบโดยไม่มี LINE</summary>
    [HttpGet("{invoiceId:int}/print")]
    public async Task<IActionResult> Print(int invoiceId, [FromServices] IReceiptService receipts, CancellationToken ct)
    {
        var row = await db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId && x.Status != InvoiceStatus.Void)
            .Select(x => new
            {
                Invoice = x,
                x.Room.RoomNumber,
                x.Room.PayeeCents,
                x.Tenant.FullName,
                PaidAmount = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount)
            }).SingleOrDefaultAsync(ct);
        if (row is null) return NotFound();
        var invoice = row.Invoice;
        var outstanding = Math.Max(invoice.TotalAmount - row.PaidAmount, 0);
        var pdf = receipts.CreateInvoice(new InvoiceDocumentData(
            invoice.InvoiceId, row.RoomNumber, row.FullName,
            invoice.BillingPeriod, invoice.UtilityPeriod, invoice.PeriodStart, invoice.PeriodEnd,
            invoice.DaysCharged, invoice.DaysInPeriod, invoice.DueDate,
            invoice.RentAmount, invoice.WaterUnits, invoice.WaterRate, invoice.WaterAmount,
            invoice.ElectricUnits, invoice.ElectricRate, invoice.ElectricAmount,
            invoice.TrashAmount, invoice.AdjustmentAmount, invoice.AdjustmentNote,
            invoice.TotalAmount, row.PaidAmount, outstanding,
            outstanding > 0 ? outstanding + row.PayeeCents : 0));
        return File(pdf, "application/pdf", $"invoice-{invoiceId}.pdf");
    }

    [HttpPost("{invoiceId:int}/send-line")]
    public async Task<IActionResult> SendLine(int invoiceId, CancellationToken ct)
    {
        if (await db.NotificationLogs.AnyAsync(x => x.InvoiceId == invoiceId && x.NotificationType == "Invoice", ct))
            return Conflict(new { message = "บิลนี้ส่งทาง LINE แล้ว" });
        var invoice = await db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId && x.Status != InvoiceStatus.Void).Select(x => new
        {
            x.InvoiceId,
            x.BillingPeriod,
            x.TotalAmount,
            x.DueDate,
            x.Room.RoomNumber,
            x.Room.PayeeCents,
            x.TenantId,
            x.Tenant.LineUserId,
            PaidAmount = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount)
        }).SingleOrDefaultAsync(ct);
        if (invoice is null) return NotFound();
        if (string.IsNullOrWhiteSpace(invoice.LineUserId)) return BadRequest(new { message = "ผู้เช่ายังไม่ได้ผูก LINE" });
        var outstanding = Math.Max(invoice.TotalAmount - invoice.PaidAmount, 0);
        if (outstanding == 0) return Conflict(new { message = "บิลนี้ชำระครบแล้ว" });
        var baseUrl = configuration["PublicLinks:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return Problem("ยังไม่ได้ตั้ง PublicLinks:BaseUrl", statusCode: 503);
        var token = signer.CreateInvoiceQrToken(invoiceId, DateTime.UtcNow.AddDays(30));
        var qrUrl = $"{baseUrl}/api/public/invoices/{invoiceId}/promptpay-qr?token={Uri.EscapeDataString(token)}";
        var result = await line.SendInvoiceAsync(new LineInvoiceMessage(
            invoice.LineUserId, invoice.InvoiceId, invoice.RoomNumber, invoice.BillingPeriod,
            outstanding, outstanding + invoice.PayeeCents, invoice.DueDate, qrUrl), ct);
        if (!result.Success) return Problem(result.Error, statusCode: 502);
        db.NotificationLogs.Add(new NotificationLog
        {
            InvoiceId = invoiceId,
            TenantId = invoice.TenantId,
            NotificationType = "Invoice",
            ExternalMessageId = result.ExternalMessageId
        });
        db.AuditLogs.Add(Audit("Invoice", invoiceId.ToString(), "SendLine", null, "Invoice"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "ส่งบิลทาง LINE แล้ว" });
    }
}
