namespace RentalManager.Core.Entities;

public sealed class AuditLog
{
    public int AuditId { get; set; }
    public required string EntityName { get; set; }
    public required string EntityKey { get; set; }
    public required string FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public required string ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}
