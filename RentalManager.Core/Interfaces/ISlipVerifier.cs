namespace RentalManager.Core.Interfaces;

public interface ISlipVerifier
{
    string Name { get; }
    Task<SlipVerificationResult> VerifyAsync(
        Stream image,
        decimal expectedAmount,
        CancellationToken cancellationToken = default);
}

public sealed record SlipVerificationResult(
    bool IsVerified,
    decimal? Amount,
    DateTime? TransferredAt,
    string? TransactionReference,
    string? FailureReason = null);
