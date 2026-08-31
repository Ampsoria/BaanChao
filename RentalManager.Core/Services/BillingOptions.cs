namespace RentalManager.Core.Services;

/// <summary>
/// ค่าตั้งต้นของการวางบิล อ่านจาก appsettings ส่วน "Billing"
/// ห้าม hardcode ค่าพวกนี้ในโค้ด (CLAUDE.md ข้อ 4 และ 11)
/// </summary>
public sealed class BillingOptions
{
    /// <summary>วันครบกำหนดชำระของเดือน ใช้เมื่อยังไม่มีนโยบายกำหนดไว้</summary>
    public byte DueDay { get; set; } = 5;

    /// <summary>ระยะพักขั้นต่ำ อยู่ไม่ครบเท่านี้ = ริบมัดจำส่วนที่เหลือ</summary>
    public byte MinimumStayMonths { get; set; } = 5;
}
