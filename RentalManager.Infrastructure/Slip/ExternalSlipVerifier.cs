using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RentalManager.Core.Interfaces;

namespace RentalManager.Infrastructure.Slip;

public sealed class ExternalSlipVerifier(HttpClient client, IOptions<ExternalSlipVerifierOptions> options) : ISlipVerifier
{
    public string Name => "ExternalApi";

    public async Task<SlipVerificationResult> VerifyAsync(
        Stream image, decimal expectedAmount, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Endpoint) || string.IsNullOrWhiteSpace(settings.ApiKey))
            return new SlipVerificationResult(false, null, null, null, "External slip API ยังไม่ได้ตั้งค่า");

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(image), "file", "slip.jpg");
        request.Content = content;
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new SlipVerificationResult(false, null, null, null, $"External API ตอบกลับ {(int)response.StatusCode}");

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        var amount = FindDecimal(root, "amount") ?? FindDecimal(root, "paidAmount");
        var reference = FindString(root, "transRef") ?? FindString(root, "reference") ?? FindString(root, "transactionId");
        var transferredAt = FindDate(root, "transDate") ?? FindDate(root, "transferredAt");
        var verified = amount.HasValue && Math.Abs(amount.Value - expectedAmount) < 0.01m && !string.IsNullOrWhiteSpace(reference);
        return new SlipVerificationResult(verified, amount, transferredAt, reference,
            verified ? null : "ข้อมูลจาก External API ไม่ตรงกับยอดที่ต้องชำระ");
    }

    private static decimal? FindDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static string? FindString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : null;

    private static DateTime? FindDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && DateTime.TryParse(
            value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date.ToUniversalTime() : null;
}
