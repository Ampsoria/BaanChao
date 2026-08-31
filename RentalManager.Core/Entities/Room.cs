namespace RentalManager.Core.Entities;

public sealed class Room
{
    public int RoomId { get; set; }
    public required string RoomNumber { get; set; }
    public decimal MonthlyRent { get; set; }
    public decimal PayeeCents { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Tenant> Tenants { get; set; } = [];
    public List<MeterReading> MeterReadings { get; set; } = [];
}
