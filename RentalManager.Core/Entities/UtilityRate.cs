namespace RentalManager.Core.Entities;

public sealed class UtilityRate
{
    public int RateId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public decimal WaterPerUnit { get; set; }
    public decimal ElectricPerUnit { get; set; }
    public decimal TrashPerMonth { get; set; }
    public string? Note { get; set; }
}
