namespace RentalManager.Core.Entities;

public sealed class NotificationLog
{
    public int NotificationId { get; set; }
    public int TenantId { get; set; }
    public int? InvoiceId { get; set; }
    public required string NotificationType { get; set; }
    public string? ExternalMessageId { get; set; }
    public DateTime SentAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}
