namespace RentalManager.Core.Entities;

public sealed class Tenant
{
    public int TenantId { get; set; }
    public int RoomId { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public string? LineUserId { get; set; }
    public DateOnly MovedInAt { get; set; }
    public DateOnly? MovedOutAt { get; set; }
    public decimal DepositAmount { get; set; }
    public DateOnly? DepositReceivedAt { get; set; }
    public DateOnly? DepositRefundedAt { get; set; }
    public byte MinimumStayMonths { get; set; } = 5;
    public Room Room { get; set; } = null!;
    public List<Invoice> Invoices { get; set; } = [];
}
