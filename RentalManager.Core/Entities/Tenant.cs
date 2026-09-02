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

    // ช่องทางรับบิล: Line | Paper — ระบบต้องทำงานได้ครบแม้ไม่มี LINE
    public string PreferredChannel { get; set; } = TenantChannels.Paper;
    public Room Room { get; set; } = null!;
    public List<Invoice> Invoices { get; set; } = [];
    public List<MeterCheckpoint> MeterCheckpoints { get; set; } = [];
}

public static class TenantChannels
{
    public const string Line = "Line";
    public const string Paper = "Paper";

    public static bool IsValid(string? value) => value is Line or Paper;
}
