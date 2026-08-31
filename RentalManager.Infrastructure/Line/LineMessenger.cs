using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RentalManager.Core.Interfaces;

namespace RentalManager.Infrastructure.Line;

public sealed class LineMessenger(HttpClient client, IOptions<LineOptions> options) : ILineMessenger
{
    public Task<LineSendResult> SendTextAsync(string lineUserId, string message, CancellationToken cancellationToken = default) =>
        PushAsync(lineUserId, [new { type = "text", text = message }], cancellationToken);

    public Task<LineSendResult> SendInvoiceAsync(LineInvoiceMessage invoice, CancellationToken cancellationToken = default)
    {
        var text = $"บิลห้อง {invoice.RoomNumber} งวด {invoice.BillingPeriod}\n" +
                   $"ยอดที่ต้องชำระ {invoice.TotalAmount:N2} บาท\nยอดที่ต้องโอน {invoice.TransferAmount:N2} บาท\n" +
                   $"กรุณาชำระภายใน {invoice.DueDate:dd/MM/yyyy}";
        var messages = new List<object> { new { type = "text", text } };
        if (!string.IsNullOrWhiteSpace(invoice.PromptPayQrUrl))
            messages.Add(new { type = "image", originalContentUrl = invoice.PromptPayQrUrl, previewImageUrl = invoice.PromptPayQrUrl });
        return PushAsync(invoice.LineUserId, messages, cancellationToken);
    }

    public async Task<byte[]> DownloadMessageContentAsync(string messageId, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get, $"https://api-data.line.me/v2/bot/message/{Uri.EscapeDataString(messageId)}/content");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<LineSendResult> PushAsync(string to, IReadOnlyCollection<object> messages, CancellationToken ct)
    {
        if (!options.Value.Enabled) return new LineSendResult(false, Error: "LINE integration ถูกปิดอยู่");
        using var request = Authorized(HttpMethod.Post, "v2/bot/message/push");
        request.Content = JsonContent.Create(new { to, messages });
        using var response = await client.SendAsync(request, ct);
        var requestId = response.Headers.TryGetValues("x-line-request-id", out var values) ? values.FirstOrDefault() : null;
        return response.IsSuccessStatusCode
            ? new LineSendResult(true, requestId)
            : new LineSendResult(false, requestId, await response.Content.ReadAsStringAsync(ct));
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ChannelAccessToken);
        return request;
    }
}
