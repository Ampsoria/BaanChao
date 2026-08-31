using RentalManager.Core.Services;

namespace RentalManager.Api.Models;

public sealed class RecordPaymentRequest
{
    public decimal PaidAmount { get; set; }
    public DateTime PaidAt { get; set; }
    public string Method { get; set; } = "PromptPay";
    public string VerificationMode { get; set; } = "Auto";
    public IFormFile? Slip { get; set; }
}

public sealed record LoginRequest(string Username, string Password);
public sealed record CreateRoomRequest(string RoomNumber, decimal MonthlyRent);
public sealed record CreateRateRequest(DateOnly EffectiveFrom, decimal Water, decimal Electric, decimal Trash, string? Note);
public sealed record UpdateRentRequest(decimal MonthlyRent);
public sealed record CreatePolicyRequest(DateOnly EffectiveFrom, byte GraceDays, string LateFeeType, decimal LateFeeAmount, decimal? LateFeeCap, string? Note);
public sealed record GenerateInvoicesRequest(string BillingPeriod);
public sealed record UpdateMeterRequest(DateOnly ReadAt, decimal WaterCurrent, decimal ElectricCurrent);
public sealed record MoveInPreviewRequest(int RoomId, DateOnly MovedInAt);
public sealed record UpdateChannelRequest(string PreferredChannel);
public sealed record MoveOutRequest(DateOnly MoveOutDate, decimal WaterFinal, decimal ElectricFinal, IReadOnlyCollection<SettlementDeductionQuote>? Deductions);
public sealed record PreviewInvoiceRequest(
    int RoomId, string BillingPeriod, DateOnly? MovedInAt,
    decimal WaterPrevious, decimal WaterCurrent, decimal ElectricPrevious, decimal ElectricCurrent,
    decimal WaterRate, decimal ElectricRate, decimal Trash);
