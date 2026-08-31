namespace RentalManager.Core.Services;

public sealed record MoveInCommand(
    int RoomId,
    string Name,
    string? Phone,
    DateOnly MovedInAt,
    decimal WaterReading,
    decimal ElectricReading,
    string PreferredChannel = "Paper");

public sealed record MoveOutCommand(
    int TenantId,
    DateOnly MoveOutDate,
    decimal WaterFinal,
    decimal ElectricFinal,
    IReadOnlyCollection<SettlementDeductionQuote> Deductions);

public sealed record MeterReadingCommand(
    int RoomId,
    string BillingPeriod,
    DateOnly ReadAt,
    decimal WaterCurrent,
    decimal ElectricCurrent);

public sealed record OperationResult<T>(T Value, string Message);
