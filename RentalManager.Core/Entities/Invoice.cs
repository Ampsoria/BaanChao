namespace RentalManager.Core.Entities;

public enum InvoiceStatus { Unpaid, Paid, Partial, Void }

public sealed class Invoice
{
    public int InvoiceId { get; set; }
    public int RoomId { get; set; }
    public int TenantId { get; set; }
    public required string BillingPeriod { get; set; }

    // เดือนของค่าน้ำ-ค่าไฟ (ย้อนหลัง 1 เดือนจาก BillingPeriod)
    // NULL = บิลใบแรกตอนย้ายเข้า หรืองวดนั้นยังไม่มีเลขมิเตอร์
    public string? UtilityPeriod { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public short DaysCharged { get; set; }
    public byte DaysInPeriod { get; set; }
    public bool IsProrated { get; private set; }
    public decimal RentAmount { get; set; }
    public decimal WaterUnits { get; set; }
    public decimal WaterRate { get; set; }
    public decimal ElectricUnits { get; set; }
    public decimal ElectricRate { get; set; }
    public decimal TrashAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public string? AdjustmentNote { get; set; }
    public decimal WaterAmount { get; private set; }
    public decimal ElectricAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public Room Room { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public List<Payment> Payments { get; set; } = [];
}
