namespace RentalManager.Core.Entities;

public sealed class MoveOutSettlement
{
    public int SettlementId { get; set; }
    public int TenantId { get; set; }
    public DateOnly MoveOutDate { get; set; }
    public DateTime SettledAt { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal FinalWaterAmount { get; set; }
    public decimal FinalElectricAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public bool IsForfeited { get; set; }
    public string? ForfeitReason { get; set; }
    public decimal MonthsStayed { get; set; }
    public decimal TotalDeducted { get; private set; }
    public decimal RefundAmount { get; private set; }
    public decimal AmountDueFromTenant { get; private set; }
    public decimal ForfeitedAmount { get; private set; }
    public DateTime? RefundedAt { get; set; }
    public string? RefundMethod { get; set; }
    public string? Note { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public List<SettlementDeduction> Deductions { get; set; } = [];
}

public sealed class SettlementDeduction
{
    public int DeductionId { get; set; }
    public int SettlementId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string? PhotoUrl { get; set; }
    public MoveOutSettlement Settlement { get; set; } = null!;
}
