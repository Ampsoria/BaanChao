namespace RentalManager.Core.Interfaces;

public interface ILineMessenger
{
    Task<LineSendResult> SendTextAsync(string lineUserId, string message, CancellationToken cancellationToken = default);
    Task<LineSendResult> SendInvoiceAsync(LineInvoiceMessage invoice, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadMessageContentAsync(string messageId, CancellationToken cancellationToken = default);
}

public sealed record LineInvoiceMessage(
    string LineUserId,
    int InvoiceId,
    string RoomNumber,
    string BillingPeriod,
    decimal TotalAmount,
    decimal TransferAmount,
    DateOnly DueDate,
    string? PromptPayQrUrl);

public sealed record LineSendResult(bool Success, string? ExternalMessageId = null, string? Error = null);
