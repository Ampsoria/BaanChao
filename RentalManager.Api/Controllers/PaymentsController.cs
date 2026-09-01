using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Models;
using RentalManager.Core.Entities;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Slip;

namespace RentalManager.Api.Controllers;

[Route("api/admin/invoices")]
public sealed class PaymentsController(
    RentalDbContext db,
    IFileStorage storage,
    LocalSlipVerifier localVerifier,
    ExternalSlipVerifier externalVerifier,
    IPromptPayService promptPay,
    IReceiptService receipts,
    IConfiguration configuration) : AdminControllerBase
{
    [HttpPost("{invoiceId:int}/payments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> RecordPayment(
        int invoiceId, [FromForm] RecordPaymentRequest request, CancellationToken ct)
    {
        if (request.PaidAmount <= 0) return BadRequest(new { message = "ยอดชำระต้องมากกว่า 0" });
        if (string.IsNullOrWhiteSpace(request.Method) || request.Method.Length > 20)
            return BadRequest(new { message = "วิธีชำระต้องมีความยาวไม่เกิน 20 ตัวอักษร" });
        if (!string.Equals(request.VerificationMode, "Auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.VerificationMode, "Manual", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "verificationMode ต้องเป็น Auto หรือ Manual" });
        if (request.PaidAt == default)
            return BadRequest(new { message = "กรุณาระบุเวลาชำระ" });
        var invoice = await db.Invoices.Include(x => x.Payments).SingleOrDefaultAsync(x => x.InvoiceId == invoiceId, ct);
        if (invoice is null || invoice.Status == InvoiceStatus.Void) return NotFound();

        StoredFile? stored = null;
        SlipVerificationResult? verification = null;
        try
        {
            if (request.Slip is not null)
            {
                await using var input = request.Slip.OpenReadStream();
                stored = await storage.SaveSlipAsync(input, request.Slip.ContentType, DateTime.UtcNow, ct);
                if (await db.Payments.AnyAsync(x => x.SlipHash == stored.Sha256, ct))
                    throw new InvalidDataException("สลิปนี้เคยถูกบันทึกแล้ว");

                if (request.VerificationMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    verification = new SlipVerificationResult(true, request.PaidAmount, request.PaidAt, null);
                }
                else
                {
                    await using var verifyStream = await storage.OpenReadAsync(stored.RelativePath, ct);
                    verification = await externalVerifier.VerifyAsync(verifyStream, request.PaidAmount, ct);
                    if (!verification.IsVerified)
                    {
                        verifyStream.Position = 0;
                        verification = await localVerifier.VerifyAsync(verifyStream, request.PaidAmount, ct);
                    }
                }
            }

            var isManual = request.VerificationMode.Equals("Manual", StringComparison.OrdinalIgnoreCase);
            var verifiedBy = isManual ? "Manual" : verification?.IsVerified == true ? "Auto" : null;
            var isVerified = isManual || verification?.IsVerified == true;
            var payment = new Payment
            {
                InvoiceId = invoiceId,
                PaidAmount = request.PaidAmount,
                PaidAt = ToUtc(request.PaidAt),
                Method = request.Method,
                SlipImageUrl = stored?.RelativePath,
                SlipHash = stored?.Sha256,
                SlipRef = verification?.TransactionReference,
                VerifiedBy = verifiedBy,
                VerificationStatus = isVerified ? "Verified" : "Pending",
                VerificationNote = verification?.FailureReason
            };
            db.Payments.Add(payment);
            var paidTotal = invoice.Payments.Where(x => x.VerificationStatus == "Verified").Sum(x => x.PaidAmount) +
                            (isVerified ? request.PaidAmount : 0);
            invoice.Status = paidTotal >= invoice.TotalAmount
                ? InvoiceStatus.Paid
                : paidTotal > 0 ? InvoiceStatus.Partial : InvoiceStatus.Unpaid;
            db.AuditLogs.Add(Audit("Payment", invoiceId.ToString(CultureInfo.InvariantCulture), "Create", null,
                $"{request.PaidAmount:0.00}/{payment.VerificationStatus}"));
            await db.SaveChangesAsync(ct);
            return Created($"/api/admin/invoices/{invoiceId}/payments/{payment.PaymentId}", new
            {
                payment.PaymentId,
                payment.VerificationStatus,
                payment.VerificationNote,
                invoice.Status
            });
        }
        catch
        {
            if (stored is not null) await storage.DeleteAsync(stored.RelativePath, ct);
            throw;
        }
    }

    private static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"));
    }

    [HttpPost("payments/{paymentId:int}/verify")]
    public async Task<IActionResult> VerifyManually(int paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.Include(x => x.Invoice).ThenInclude(x => x.Payments)
            .SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct);
        if (payment is null) return NotFound();
        if (payment.Invoice.Status == InvoiceStatus.Void)
            return Conflict(new { message = "ยืนยันการชำระของบิลที่ยกเลิกแล้วไม่ได้" });
        if (payment.VerificationStatus == "Verified")
            return Ok(new { message = "รายการนี้ยืนยันการชำระแล้ว", payment.Invoice.Status });
        var previousStatus = payment.VerificationStatus;
        payment.VerificationStatus = "Verified";
        payment.VerifiedBy = "Manual";
        payment.VerificationNote = null;
        var total = payment.Invoice.Payments.Where(x => x.VerificationStatus == "Verified").Sum(x => x.PaidAmount);
        payment.Invoice.Status = total >= payment.Invoice.TotalAmount ? InvoiceStatus.Paid : InvoiceStatus.Partial;
        db.AuditLogs.Add(Audit("Payment", paymentId.ToString(CultureInfo.InvariantCulture), "Verify", previousStatus, "Verified"));
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "ยืนยันการชำระเงินแล้ว", payment.Invoice.Status });
    }

    [HttpGet("{invoiceId:int}/promptpay-qr")]
    public async Task<IActionResult> PromptPayQr(int invoiceId, CancellationToken ct)
    {
        var row = await db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId && x.Status != InvoiceStatus.Void).Select(x => new
        {
            x.TotalAmount,
            x.Room.PayeeCents,
            PaidAmount = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount)
        }).SingleOrDefaultAsync(ct);
        if (row is null) return NotFound();
        var outstanding = Math.Max(row.TotalAmount - row.PaidAmount, 0);
        if (outstanding == 0) return Conflict(new { message = "บิลนี้ชำระครบแล้ว" });
        var target = configuration["PromptPay:Target"];
        if (string.IsNullOrWhiteSpace(target)) return Problem("ยังไม่ได้ตั้ง PromptPay:Target", statusCode: 503);
        return File(promptPay.CreateQrPng(target, outstanding + row.PayeeCents), "image/png", $"invoice-{invoiceId}-promptpay.png");
    }

    [HttpGet("payments/{paymentId:int}/slip")]
    public async Task<IActionResult> GetSlip(int paymentId, CancellationToken ct)
    {
        var path = await db.Payments.AsNoTracking().Where(x => x.PaymentId == paymentId)
            .Select(x => x.SlipImageUrl).SingleOrDefaultAsync(ct);
        if (path is null) return NotFound();
        return File(await storage.OpenReadAsync(path, ct), "image/jpeg");
    }

    [HttpGet("payments/{paymentId:int}/receipt.pdf")]
    public async Task<IActionResult> Receipt(int paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking()
            .Where(x => x.PaymentId == paymentId && x.VerificationStatus == "Verified").Select(x => new ReceiptData(
            x.PaymentId, x.Invoice.Room.RoomNumber, x.Invoice.Tenant.FullName, x.Invoice.BillingPeriod,
            x.PaidAmount, x.PaidAt, x.Method)).SingleOrDefaultAsync(ct);
        return payment is null ? NotFound() : File(receipts.CreateReceipt(payment), "application/pdf", $"receipt-{paymentId}.pdf");
    }
}
