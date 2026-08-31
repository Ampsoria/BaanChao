namespace RentalManager.Core.Interfaces;

public interface IReceiptService
{
    byte[] CreateReceipt(ReceiptData data);
    byte[] CreateSettlementStatement(SettlementStatementData data);
}

public sealed record ReceiptData(
    int PaymentId, string RoomNumber, string TenantName, string BillingPeriod,
    decimal PaidAmount, DateTime PaidAtUtc, string Method);

public sealed record SettlementStatementData(
    int SettlementId, string RoomNumber, string TenantName, DateOnly MoveOutDate,
    decimal DepositAmount, decimal FinalWaterAmount, decimal FinalElectricAmount,
    decimal OutstandingAmount, IReadOnlyCollection<SettlementStatementDeduction> Deductions,
    bool IsForfeited, decimal ForfeitedAmount, decimal RefundAmount, decimal AmountDueFromTenant);

public sealed record SettlementStatementDeduction(string Description, decimal Amount, byte[]? Photo = null);
