using Microsoft.EntityFrameworkCore;
using RentalManager.Core.Entities;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;
using RentalManager.Core.Services;

namespace RentalManager.Api.Services;

public sealed class BillingAutomationWorker(
    IServiceScopeFactory scopeFactory,
    ILineMessenger line,
    PublicLinkSigner signer,
    IConfiguration configuration,
    ILogger<BillingAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Automation:Enabled", true)) return;
        await RunOnce(stoppingToken);
        var minutes = Math.Clamp(configuration.GetValue("Automation:IntervalMinutes", 15), 1, 1440);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await RunOnce(stoppingToken);
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();
            var operations = scope.ServiceProvider.GetRequiredService<RentalOperationsService>();
            var bangkokNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Bangkok");
            var today = DateOnly.FromDateTime(bangkokNow.Date);
            var period = today.ToString("yyyy-MM");
            if (today.Day == 1)
                await operations.GenerateMonthlyInvoicesAsync(period, "Automation", ct);
            await ApplyLateFees(db, today, ct);
            await SendNotifications(db, today, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Billing automation cycle failed");
        }
    }

    private static async Task ApplyLateFees(RentalDbContext db, DateOnly today, CancellationToken ct)
    {
        var invoices = await db.Invoices.Include(x => x.Payments)
            .Where(x => x.Status != InvoiceStatus.Paid && x.Status != InvoiceStatus.Void && x.DueDate < today)
            .ToListAsync(ct);
        foreach (var invoice in invoices)
        {
            var paid = invoice.Payments.Where(x => x.VerificationStatus == "Verified").Sum(x => x.PaidAmount);
            if (paid >= invoice.TotalAmount) continue;
            var periodStart = DateOnly.ParseExact(invoice.BillingPeriod + "-01", "yyyy-MM-dd");
            var policy = await db.BillingPolicies.AsNoTracking().Where(x => x.EffectiveFrom <= periodStart)
                .OrderByDescending(x => x.EffectiveFrom).FirstAsync(ct);
            var lateDays = today.DayNumber - invoice.DueDate.DayNumber;
            var fee = BillingCalculator.CalculateLateFee(
                policy.LateFeeType, policy.LateFeeAmount, policy.LateFeeCap, invoice.DueDate, today);
            if (invoice.AdjustmentNote is not null && !invoice.AdjustmentNote.StartsWith("ค่าปรับล่าช้า", StringComparison.Ordinal))
                continue;
            if (invoice.AdjustmentAmount == fee &&
                (fee == 0 || invoice.AdjustmentNote?.StartsWith($"ค่าปรับล่าช้า {lateDays} วัน", StringComparison.Ordinal) == true))
                continue;
            var previousFee = invoice.AdjustmentAmount;
            invoice.AdjustmentAmount = fee;
            invoice.AdjustmentNote = fee > 0 ? $"ค่าปรับล่าช้า {lateDays} วัน ตามนโยบาย {policy.EffectiveFrom:yyyy-MM-dd}" : null;
            db.AuditLogs.Add(new AuditLog
            {
                EntityName = "Invoice",
                EntityKey = invoice.InvoiceId.ToString(),
                FieldName = "LateFee",
                OldValue = previousFee.ToString("0.00"),
                NewValue = fee.ToString("0.00"),
                ChangedBy = "Automation"
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SendNotifications(RentalDbContext db, DateOnly today, CancellationToken ct)
    {
        if (!configuration.GetValue("Line:Enabled", false)) return;
        var baseUrl = configuration["PublicLinks:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        var invoices = await db.Invoices.Include(x => x.Payments).Include(x => x.Room).Include(x => x.Tenant)
            .Where(x => x.Status != InvoiceStatus.Paid && x.Status != InvoiceStatus.Void && x.Tenant.LineUserId != null)
            .ToListAsync(ct);
        foreach (var invoice in invoices)
        {
            var paid = invoice.Payments.Where(x => x.VerificationStatus == "Verified").Sum(x => x.PaidAmount);
            if (paid >= invoice.TotalAmount) continue;
            var notificationType = invoice.DueDate < today ? "Overdue" : "Invoice";
            if (await db.NotificationLogs.AnyAsync(x => x.InvoiceId == invoice.InvoiceId && x.NotificationType == notificationType, ct))
                continue;
            LineSendResult result;
            if (notificationType == "Invoice")
            {
                var token = signer.CreateInvoiceQrToken(invoice.InvoiceId, DateTime.UtcNow.AddDays(30));
                var qrUrl = $"{baseUrl}/api/public/invoices/{invoice.InvoiceId}/promptpay-qr?token={Uri.EscapeDataString(token)}";
                result = await line.SendInvoiceAsync(new LineInvoiceMessage(
                    invoice.Tenant.LineUserId!, invoice.InvoiceId, invoice.Room.RoomNumber, invoice.BillingPeriod,
                    invoice.TotalAmount - paid, invoice.TotalAmount - paid + invoice.Room.PayeeCents,
                    invoice.DueDate, qrUrl), ct);
            }
            else
            {
                result = await line.SendTextAsync(invoice.Tenant.LineUserId!,
                    $"แจ้งเตือนห้อง {invoice.Room.RoomNumber}: บิลงวด {invoice.BillingPeriod} เลยกำหนดชำระแล้ว ยอดค้าง {(invoice.TotalAmount - paid):N2} บาท", ct);
            }
            if (result.Success)
            {
                db.NotificationLogs.Add(new NotificationLog
                {
                    InvoiceId = invoice.InvoiceId,
                    TenantId = invoice.TenantId,
                    NotificationType = notificationType,
                    ExternalMessageId = result.ExternalMessageId
                });
                db.AuditLogs.Add(new AuditLog
                {
                    EntityName = "Invoice",
                    EntityKey = invoice.InvoiceId.ToString(),
                    FieldName = "SendLine",
                    NewValue = notificationType,
                    ChangedBy = "Automation"
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
