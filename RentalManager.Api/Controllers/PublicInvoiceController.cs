using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Services;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;
using RentalManager.Core.Entities;

namespace RentalManager.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/invoices")]
public sealed class PublicInvoiceController(
    RentalDbContext db, IPromptPayService promptPay, PublicLinkSigner signer, IConfiguration configuration) : ControllerBase
{
    [HttpGet("{invoiceId:int}/promptpay-qr")]
    public async Task<IActionResult> PromptPayQr(int invoiceId, string token, CancellationToken ct)
    {
        if (!signer.ValidateInvoiceQrToken(invoiceId, token)) return Unauthorized();
        var row = await db.Invoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId && x.Status != InvoiceStatus.Void)
            .Select(x => new
            {
                x.TotalAmount,
                x.Room.PayeeCents,
                PaidAmount = x.Payments.Where(p => p.VerificationStatus == "Verified").Sum(p => p.PaidAmount)
            }).SingleOrDefaultAsync(ct);
        if (row is null) return NotFound();
        var outstanding = Math.Max(row.TotalAmount - row.PaidAmount, 0);
        if (outstanding == 0) return NotFound();
        var target = configuration["PromptPay:Target"];
        if (string.IsNullOrWhiteSpace(target)) return NotFound();
        return File(promptPay.CreateQrPng(target, outstanding + row.PayeeCents), "image/png");
    }
}
