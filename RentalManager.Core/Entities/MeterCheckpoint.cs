namespace RentalManager.Core.Entities;

/// <summary>
/// เลขมิเตอร์ ณ จุดเปลี่ยนผู้ครอบครองห้อง เก็บแยกจาก MeterReading รายเดือน
/// เพื่อไม่ให้หน่วยที่เกิดระหว่างห้องว่างถูกคิดกับผู้เช่าคนเก่าหรือคนใหม่
/// </summary>
public sealed class MeterCheckpoint
{
    public int MeterCheckpointId { get; set; }
    public int RoomId { get; set; }
    public int TenantId { get; set; }
    public DateOnly RecordedAt { get; set; }
    public required string Kind { get; set; }
    public decimal WaterReading { get; set; }
    public decimal ElectricReading { get; set; }
    public Room Room { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}

public static class MeterCheckpointKinds
{
    public const string MoveIn = "MoveIn";
    public const string MoveOut = "MoveOut";
    public const string ImportedBaseline = "ImportedBaseline";
}
