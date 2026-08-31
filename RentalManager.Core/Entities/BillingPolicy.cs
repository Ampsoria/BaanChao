namespace RentalManager.Core.Entities;

public enum LateFeeType { None, PerDay, Flat }

public sealed class BillingPolicy
{
    public int PolicyId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public byte GraceDays { get; set; } = 5;
    public LateFeeType LateFeeType { get; set; }
    public decimal LateFeeAmount { get; set; }
    public decimal? LateFeeCap { get; set; }
    public string? Note { get; set; }
}
