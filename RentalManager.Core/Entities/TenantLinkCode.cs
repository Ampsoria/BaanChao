namespace RentalManager.Core.Entities;

public sealed class TenantLinkCode
{
    public int LinkCodeId { get; set; }
    public int TenantId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
}
