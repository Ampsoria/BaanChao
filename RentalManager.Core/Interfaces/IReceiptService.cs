namespace RentalManager.Core.Interfaces;

public interface IReceiptService
{
    byte[] CreateReceipt(ReceiptData data);
    byte[] CreateSettlementStatement(SettlementStatementData data);
    byte[] CreateInvoice(InvoiceDocumentData data);
}

/// <summary>
/// บิลสำหรับพิมพ์ให้ผู้เช่าที่รับบิลเป็นกระดาษ
/// ต้องแสดงทั้งงวดค่าเช่าและงวดค่าน้ำ-ค่าไฟ เพราะเป็นคนละเดือนกัน (CLAUDE.md ข้อ 4)
/// </summary>
public sealed record InvoiceDocumentData(
    int InvoiceId, string RoomNumber, string TenantName,
    string BillingPeriod, string? UtilityPeriod, DateOnly PeriodStart, DateOnly PeriodEnd,
    short DaysCharged, byte DaysInPeriod, DateOnly DueDate,
    decimal RentAmount, decimal WaterUnits, decimal WaterRate, decimal WaterAmount,
    decimal ElectricUnits, decimal ElectricRate, decimal ElectricAmount,
    decimal TrashAmount, decimal AdjustmentAmount, string? AdjustmentNote,
    decimal TotalAmount, decimal PaidAmount, decimal Outstanding, decimal TransferAmount);

public sealed record ReceiptData(
    int PaymentId, string RoomNumber, string TenantName, string BillingPeriod,
    decimal PaidAmount, DateTime PaidAtUtc, string Method);

public sealed record SettlementStatementData(
    int SettlementId, string RoomNumber, string TenantName, DateOnly MoveOutDate,
    decimal DepositAmount, decimal FinalWaterAmount, decimal FinalElectricAmount,
    decimal OutstandingAmount, IReadOnlyCollection<SettlementStatementDeduction> Deductions,
    bool IsForfeited, decimal ForfeitedAmount, decimal RefundAmount, decimal AmountDueFromTenant);

public sealed record SettlementStatementDeduction(string Description, decimal Amount, byte[]? Photo = null);
