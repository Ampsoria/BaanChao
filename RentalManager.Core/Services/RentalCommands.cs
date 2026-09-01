namespace RentalManager.Core.Services;

public sealed record MoveInCommand(
    int RoomId,
    string Name,
    string? Phone,
    DateOnly MovedInAt,
    decimal WaterReading,
    decimal ElectricReading,
    string PreferredChannel = "Paper");

public sealed record ImportExistingTenantCommand(
    int RoomId,
    string Name,
    string? Phone,
    DateOnly MovedInAt,
    decimal DepositAmount,
    DateOnly? DepositReceivedAt,
    byte MinimumStayMonths,
    string MeterBillingPeriod,
    DateOnly MeterReadAt,
    decimal WaterReading,
    decimal ElectricReading);

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
