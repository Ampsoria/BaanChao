using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using RentalManager.Api.Services;
using RentalManager.Core.Interfaces;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Documents;
using RentalManager.Infrastructure.PromptPay;
using RentalManager.Infrastructure.Storage;
using RentalManager.Infrastructure.Line;
using RentalManager.Infrastructure.Slip;
using System.Net;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RentalManager.Tests;

public sealed class InfrastructureServiceTests
{
    [Fact]
    public void PromptPayPayload_HasEmvFieldsAmountAndValidCrc()
    {
        var service = new PromptPayService();
        var payload = service.CreatePayload("081-234-5678", 1840.01m);

        Assert.StartsWith("000201010212", payload);
        Assert.Contains("5303764", payload);
        Assert.Contains("54071840.01", payload);
        Assert.Contains("0066812345678", payload);
        Assert.Equal(Crc16(payload[..^4]), payload[^4..]);
        Assert.True(service.CreateQrPng("0812345678", 1840.01m).Length > 100);
    }

    [Fact]
    public async Task FileStorage_ResizesHashesAndBlocksTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rental-storage-{Guid.NewGuid():N}");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { SlipRoot = root }));
            using var image = new Image<Rgba32>(1600, 100, Color.White);
            await using var input = new MemoryStream();
            await image.SaveAsPngAsync(input, ct);
            input.Position = 0;
            var saved = await storage.SaveSlipAsync(input, "image/png", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ct);

            Assert.StartsWith("2026/09/", saved.RelativePath);
            Assert.Equal(64, saved.Sha256.Length);
            await using var result = await storage.OpenReadAsync(saved.RelativePath, ct);
            using var resized = await Image.LoadAsync(result, ct);
            Assert.Equal(1200, resized.Width);
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.OpenReadAsync("../secret.txt", ct));
            await using var fakeImage = new MemoryStream([1, 2, 3, 4]);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                storage.SaveSlipAsync(fakeImage, "image/jpeg", DateTime.UtcNow, ct));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReceiptAndSettlement_AreValidPdfDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        using var evidence = new Image<Rgba32>(320, 180, Color.LightGray);
        await using var evidenceStream = new MemoryStream();
        await evidence.SaveAsJpegAsync(evidenceStream, ct);
        var service = new ReceiptService();
        var receipt = service.CreateReceipt(new ReceiptData(1, "2", "แอมป์", "2026-09", 2480.02m, DateTime.UtcNow, "PromptPay"));
        var settlement = service.CreateSettlementStatement(new SettlementStatementData(
            1, "2", "แอมป์", new DateOnly(2026, 9, 1), 2000, 80, 300, 0,
            [new SettlementStatementDeduction("Lost key", 100, evidenceStream.ToArray())], false, 0, 1520, 0));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(receipt, 0, 4));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(settlement, 0, 4));
    }

    [Fact]
    public void PublicLinkSigner_RejectsTamperingAndDifferentInvoice()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicLinks:SigningKey"] = "0123456789abcdef0123456789abcdef"
        }).Build();
        var signer = new PublicLinkSigner(configuration);
        var token = signer.CreateInvoiceQrToken(42, DateTime.UtcNow.AddMinutes(5));

        Assert.True(signer.ValidateInvoiceQrToken(42, token));
        Assert.False(signer.ValidateInvoiceQrToken(41, token));
        Assert.False(signer.ValidateInvoiceQrToken(42, token + "x"));
    }

    [Fact]
    public void EfModel_HasNoChangesAfterLatestMigration()
    {
        using var db = new RentalDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void LineSignatureVerifier_RejectsTamperedBodyAndMalformedSignature()
    {
        const string body = "{\"events\":[]}";
        const string secret = "channel-secret";
        var signature = Convert.ToBase64String(HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(body)));

        Assert.True(LineSignatureVerifier.Verify(body, signature, secret));
        Assert.False(LineSignatureVerifier.Verify(body + " ", signature, secret));
        Assert.False(LineSignatureVerifier.Verify(body, "not-base64", secret));
    }

    [Fact]
    public async Task ExternalSlipVerifier_MapsVerifiedResponseAndChecksExpectedAmount()
    {
        var handler = new StubHttpHandler("""{"data":{"amount":2480.02,"transRef":"TX-001","transferredAt":"2026-09-01T03:00:00Z"}}""");
        var verifier = new ExternalSlipVerifier(new HttpClient(handler), Options.Create(new ExternalSlipVerifierOptions
        {
            Enabled = true,
            Endpoint = "https://slip.example/verify",
            ApiKey = "secret"
        }));
        await using var image = new MemoryStream([1, 2, 3]);
        var result = await verifier.VerifyAsync(image, 2480.02m, TestContext.Current.CancellationToken);

        Assert.True(result.IsVerified);
        Assert.Equal("TX-001", result.TransactionReference);
        Assert.Equal(2480.02m, result.Amount);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
    }

    private static string Crc16(string value)
    {
        var crc = 0xFFFF;
        foreach (var valueByte in System.Text.Encoding.ASCII.GetBytes(value))
        {
            crc ^= valueByte << 8;
            for (var i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
            crc &= 0xFFFF;
        }
        return crc.ToString("X4");
    }

    private sealed class StubHttpHandler(string json) : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
