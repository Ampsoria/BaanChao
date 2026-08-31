namespace RentalManager.Core.Entities;

public sealed class MeterReading
{
    public int ReadingId { get; set; }
    public int RoomId { get; set; }
    public required string BillingPeriod { get; set; }
    public DateOnly ReadAt { get; set; }
    public decimal WaterPrev { get; set; }
    public decimal WaterCurrent { get; set; }
    public decimal ElectricPrev { get; set; }
    public decimal ElectricCurrent { get; set; }
    public decimal WaterUnits { get; private set; }
    public decimal ElectricUnits { get; private set; }
    public Room Room { get; set; } = null!;
}
