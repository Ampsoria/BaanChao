namespace RentalManager.Core.Entities;

public sealed class Payment
{
    public int PaymentId { get; set; }
    public int InvoiceId { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime PaidAt { get; set; }
    public string Method { get; set; } = "PromptPay";
    public string? SlipImageUrl { get; set; }
    public string? SlipHash { get; set; }
    public string? SlipRef { get; set; }
    public string? VerifiedBy { get; set; }
    public string VerificationStatus { get; set; } = "Pending";
    public string? VerificationNote { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedBy { get; set; }
    public DateTime RecordedAt { get; set; }
    public Invoice Invoice { get; set; } = null!;
}
