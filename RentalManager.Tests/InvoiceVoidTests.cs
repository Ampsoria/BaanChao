using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RentalManager.Api.Controllers;
using RentalManager.Api.Models;
using RentalManager.Api.Services;
using RentalManager.Core.Entities;
using RentalManager.Core.Interfaces;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Documents;
using RentalManager.Infrastructure.PromptPay;
using RentalManager.Infrastructure.Services;
using RentalManager.Infrastructure.Slip;
using RentalManager.Infrastructure.Storage;
using Xunit;

namespace RentalManager.Tests;

public sealed class InvoiceVoidTests
{
    [Fact]
    public async Task VoidInvoice_MarksInvoiceAndWritesAudit()
    {
        await using var db = CreateDatabase();
        var invoice = AddInvoice(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).VoidInvoice(invoice.InvoiceId,
            new VoidInvoiceRequest("เลขมิเตอร์ผิด"), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(InvoiceStatus.Void, invoice.Status);
        Assert.Contains(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken),
            x => x.EntityName == "Invoice" && x.FieldName == "Void");
    }

    [Fact]
    public async Task VoidInvoice_RejectsVerifiedPayment()
    {
        await using var db = CreateDatabase();
        var invoice = AddInvoice(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.InvoiceId,
            PaidAmount = 100m,
            PaidAt = DateTime.UtcNow,
            Method = "Cash",
            VerificationStatus = "Verified",
            VerifiedBy = "test"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).VoidInvoice(invoice.InvoiceId,
            new VoidInvoiceRequest("ต้องการยกเลิก"), TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(InvoiceStatus.Unpaid, invoice.Status);
    }

    [Fact]
    public async Task VerifyPayment_RejectsPaymentBelongingToVoidInvoice()
    {
        await using var db = CreateDatabase();
        var invoice = AddInvoice(db);
        invoice.Status = InvoiceStatus.Void;
        var payment = new Payment
        {
            Invoice = invoice,
            PaidAmount = 100m,
            PaidAt = DateTime.UtcNow,
            Method = "Cash",
            VerificationStatus = "Pending"
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreatePaymentsController(db).VerifyManually(
            payment.PaymentId, TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("Pending", payment.VerificationStatus);
        Assert.Equal(InvoiceStatus.Void, invoice.Status);
        Assert.Empty(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyPayment_IsIdempotentWhenAlreadyVerified()
    {
        await using var db = CreateDatabase();
        var invoice = AddInvoice(db);
        var payment = new Payment
        {
            Invoice = invoice,
            PaidAmount = 100m,
            PaidAt = DateTime.UtcNow,
            Method = "Cash",
            VerificationStatus = "Verified",
            VerifiedBy = "test"
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreatePaymentsController(db).VerifyManually(
            payment.PaymentId, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VoidPayment_PreservesRecordAndReopensInvoice()
    {
        await using var db = CreateDatabase();
        var invoice = AddInvoice(db);
        SetComputedTotal(invoice, 1_800m);
        invoice.Status = InvoiceStatus.Paid;
        var payment = new Payment
        {
            Invoice = invoice,
            PaidAmount = 1_800m,
            PaidAt = DateTime.UtcNow,
            Method = "Cash",
            VerificationStatus = "Verified",
            VerifiedBy = "test"
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreatePaymentsController(db).VoidPayment(payment.PaymentId,
            new VoidPaymentRequest("บันทึกยอดผิด"), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Void", payment.VerificationStatus);
        Assert.Equal("บันทึกยอดผิด", payment.VoidReason);
        Assert.NotNull(payment.VoidedAt);
        Assert.Equal(InvoiceStatus.Unpaid, invoice.Status);
        Assert.Contains(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken),
            x => x.EntityName == "Payment" && x.FieldName == "Void");
    }

    [Fact]
    public async Task RecordPayment_RejectsAmountAboveOutstandingAndPayeeCents()
    {
        await using var db = CreateDatabase();
        var invoice = AddInvoice(db);
        SetComputedTotal(invoice, 1_800m);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreatePaymentsController(db).RecordPayment(invoice.InvoiceId,
            new RecordPaymentRequest
            {
                PaidAmount = 1_800.02m,
                PaidAt = DateTime.UtcNow,
                Method = "PromptPay",
                VerificationMode = "Manual"
            }, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Payments.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static Invoice AddInvoice(RentalDbContext db)
    {
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่า",
            MovedInAt = new DateOnly(2024, 1, 1),
            DepositAmount = 1_800m
        };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        var invoice = new Invoice
        {
            RoomId = 1,
            TenantId = tenant.TenantId,
            BillingPeriod = "2026-09",
            DueDate = new DateOnly(2026, 9, 5),
            PeriodStart = new DateOnly(2026, 9, 1),
            PeriodEnd = new DateOnly(2026, 9, 30),
            DaysCharged = 30,
            DaysInPeriod = 30,
            RentAmount = 1_800m,
            WaterRate = 20m,
            ElectricRate = 12m,
            TrashAmount = 40m
        };
        db.Invoices.Add(invoice);
        return invoice;
    }

    private static RentalDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"invoice-void-{Guid.NewGuid():N}")
            .Options;
        var db = new RentalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static InvoicesController CreateController(RentalDbContext db)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicLinks:SigningKey"] = "invoice-void-test-signing-key-32-characters"
        }).Build();
        var service = new RentalOperationsService(db, Options.Create(new BillingOptions()));
        var controller = new InvoicesController(db, service, new FakeLineMessenger(),
            new PublicLinkSigner(configuration), configuration);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "InvoiceVoidTest")], "Test"))
            }
        };
        return controller;
    }

    private static PaymentsController CreatePaymentsController(RentalDbContext db)
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions
        {
            SlipRoot = Path.Combine(Path.GetTempPath(), $"rental-void-test-{Guid.NewGuid():N}")
        }));
        var controller = new PaymentsController(
            db,
            storage,
            new LocalSlipVerifier(),
            new ExternalSlipVerifier(new HttpClient(), Options.Create(new ExternalSlipVerifierOptions())),
            new PromptPayService(),
            new ReceiptService(),
            new ConfigurationBuilder().Build());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "PaymentTest")], "Test"))
            }
        };
        return controller;
    }

    private static void SetComputedTotal(Invoice invoice, decimal total) =>
        typeof(Invoice).GetProperty(nameof(Invoice.TotalAmount))!.SetValue(invoice, total);

    private sealed class FakeLineMessenger : ILineMessenger
    {
        public Task<LineSendResult> SendTextAsync(string lineUserId, string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LineSendResult(true));
        public Task<LineSendResult> SendInvoiceAsync(LineInvoiceMessage invoice, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LineSendResult(true));
        public Task<byte[]> DownloadMessageContentAsync(string messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());
    }
}
