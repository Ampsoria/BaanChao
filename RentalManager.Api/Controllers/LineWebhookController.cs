using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentalManager.Core.Entities;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Line;
using RentalManager.Infrastructure.Slip;

namespace RentalManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/line/webhook")]
public sealed class LineWebhookController(
    RentalDbContext db,
    ILineMessenger line,
    IFileStorage storage,
    LocalSlipVerifier localVerifier,
    ExternalSlipVerifier externalVerifier,
    IOptions<LineOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        if (!LineSignatureVerifier.Verify(body, Request.Headers["x-line-signature"].ToString(), options.Value.ChannelSecret))
            return Unauthorized();

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("events", out var events)) return Ok();
        foreach (var lineEvent in events.EnumerateArray())
            await HandleEvent(lineEvent, ct);
        return Ok();
    }

    private async Task HandleEvent(JsonElement lineEvent, CancellationToken ct)
    {
        if (!lineEvent.TryGetProperty("source", out var source) ||
            !source.TryGetProperty("userId", out var userIdElement)) return;
        var userId = userIdElement.GetString();
        if (string.IsNullOrWhiteSpace(userId) || !lineEvent.TryGetProperty("message", out var message)) return;
        var type = message.GetProperty("type").GetString();
        if (type == "text") await HandleText(userId, message.GetProperty("text").GetString() ?? "", ct);
        if (type == "image") await HandleImage(userId, message.GetProperty("id").GetString()!, ct);
    }

    private async Task HandleText(string userId, string text, CancellationToken ct)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0] == "ผูกห้อง" && parts[1].Length == 6)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(parts[1])));
            var linkCode = await db.TenantLinkCodes.Include(x => x.Tenant).ThenInclude(x => x.Room)
                .SingleOrDefaultAsync(x => x.CodeHash == hash && x.UsedAt == null && x.ExpiresAt > DateTime.UtcNow, ct);
            if (linkCode is null)
            {
                await line.SendTextAsync(userId, "รหัสไม่ถูกต้องหรือหมดอายุ กรุณาขอรหัสใหม่จากผู้ดูแล", ct);
                return;
            }
            linkCode.UsedAt = DateTime.UtcNow;
            linkCode.Tenant.LineUserId = userId;
            // ผู้เช่าผูกไลน์เอง = เลือกรับบิลทางไลน์ ผู้ดูแลเปลี่ยนกลับเป็น Paper ได้ทีหลัง
            linkCode.Tenant.PreferredChannel = TenantChannels.Line;
            db.AuditLogs.Add(new AuditLog
            {
                EntityName = "Tenant",
                EntityKey = linkCode.TenantId.ToString(),
                FieldName = "LineUserId",
                NewValue = "Linked",
                ChangedBy = $"LINE:{userId}"
            });
            await db.SaveChangesAsync(ct);
            await line.SendTextAsync(userId, $"ผูก LINE กับห้อง {linkCode.Tenant.Room.RoomNumber} สำเร็จ", ct);
            return;
        }
        await line.SendTextAsync(userId, "หากต้องการผูกห้อง ให้ส่ง: ผูกห้อง ตามด้วยรหัส 6 หลัก หรือส่งรูปสลิปเพื่อแจ้งชำระ", ct);
    }

    private async Task HandleImage(string userId, string messageId, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.LineUserId == userId && x.MovedOutAt == null, ct);
        if (tenant is null) { await line.SendTextAsync(userId, "ยังไม่ได้ผูก LINE กับห้อง", ct); return; }
        var invoice = await db.Invoices.Include(x => x.Room).Include(x => x.Payments)
            .Where(x => x.TenantId == tenant.TenantId && x.Status != InvoiceStatus.Paid && x.Status != InvoiceStatus.Void)
            .OrderBy(x => x.DueDate).FirstOrDefaultAsync(ct);
        if (invoice is null) { await line.SendTextAsync(userId, "ไม่พบบิลที่รอชำระ", ct); return; }

        var bytes = await line.DownloadMessageContentAsync(messageId, ct);
        await using var input = new MemoryStream(bytes);
        var stored = await storage.SaveSlipAsync(input, "image/jpeg", DateTime.UtcNow, ct);
        if (await db.Payments.AnyAsync(x => x.SlipHash == stored.Sha256, ct))
        {
            await storage.DeleteAsync(stored.RelativePath, ct);
            await line.SendTextAsync(userId, "สลิปนี้เคยส่งแล้ว", ct);
            return;
        }

        SlipVerificationResult verification;
        Payment payment;
        try
        {
            var paid = invoice.Payments.Where(x => x.VerificationStatus == "Verified").Sum(x => x.PaidAmount);
            var expected = Math.Max(invoice.TotalAmount - paid, 0) + invoice.Room.PayeeCents;
            await using var verifyStream = await storage.OpenReadAsync(stored.RelativePath, ct);
            verification = await externalVerifier.VerifyAsync(verifyStream, expected, ct);
            var verifier = "ExternalApi";
            if (!verification.IsVerified)
            {
                verifyStream.Position = 0;
                verification = await localVerifier.VerifyAsync(verifyStream, expected, ct);
                verifier = "Local";
            }
            if (!string.IsNullOrWhiteSpace(verification.TransactionReference) &&
                await db.Payments.AnyAsync(x => x.SlipRef == verification.TransactionReference, ct))
            {
                await storage.DeleteAsync(stored.RelativePath, ct);
                await line.SendTextAsync(userId, "รายการโอนนี้เคยถูกบันทึกแล้ว", ct);
                return;
            }
            payment = new Payment
            {
                InvoiceId = invoice.InvoiceId,
                PaidAmount = verification.Amount ?? expected,
                PaidAt = verification.TransferredAt ?? DateTime.UtcNow,
                SlipImageUrl = stored.RelativePath,
                SlipHash = stored.Sha256,
                SlipRef = verification.TransactionReference,
                VerifiedBy = verification.IsVerified ? verifier : null,
                VerificationStatus = verification.IsVerified ? "Verified" : "Pending",
                VerificationNote = verification.FailureReason
            };
            db.Payments.Add(payment);
            if (verification.IsVerified) invoice.Status = InvoiceStatus.Paid;
            db.AuditLogs.Add(new AuditLog
            {
                EntityName = "Payment",
                EntityKey = invoice.InvoiceId.ToString(),
                FieldName = "CreateFromLine",
                NewValue = $"{payment.PaidAmount:0.00}/{payment.VerificationStatus}",
                ChangedBy = $"LINE:{userId}"
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(stored.RelativePath, ct);
            throw;
        }
        await line.SendTextAsync(userId, verification.IsVerified
            ? $"รับสลิปและยืนยันยอด {payment.PaidAmount:N2} บาทแล้ว ขอบคุณครับ"
            : "รับสลิปแล้ว รอผู้ดูแลตรวจสอบก่อนยืนยันการชำระ", ct);
    }

}
