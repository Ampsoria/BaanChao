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
            x.BillingPeriod,
            x.DueDate,
            x.RentAmount,
            x.WaterAmount,
            x.ElectricAmount,
            x.TrashAmount,
            x.AdjustmentAmount,
            x.TotalAmount,
            PaidAmount = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount),
            Outstanding = Math.Max(x.TotalAmount - x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount), 0),
            TransferAmount = Math.Max(x.TotalAmount - x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount), 0) > 0
                ? Math.Max(x.TotalAmount - x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount), 0) + x.Room.PayeeCents
                : 0,
            x.Status,
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

    [HttpPost("{invoiceId:int}/send-line")]
    public async Task<IActionResult> SendLine(int invoiceId, CancellationToken ct)
    {
        if (await db.NotificationLogs.AnyAsync(x => x.InvoiceId == invoiceId && x.NotificationType == "Invoice", ct))
            return Conflict(new { message = "บิลนี้ส่งทาง LINE แล้ว" });
        var invoice = await db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId).Select(x => new
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
