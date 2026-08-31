namespace RentalManager.Core.Services;

using RentalManager.Core.Entities;

public static class BillingCalculator
{
    private const decimal AverageDaysPerMonth = 30.44m;

    public static decimal CalculateLateFee(
        LateFeeType type, decimal amount, decimal? cap, DateOnly dueDate, DateOnly asOfDate)
    {
        EnsureNonNegative(amount, nameof(amount));
        if (cap < 0) throw new BillingRuleException("เพดานค่าปรับต้องไม่ติดลบ");
        if (asOfDate <= dueDate || type == LateFeeType.None) return 0;
        return type switch
        {
            LateFeeType.Flat => amount,
            LateFeeType.PerDay => Math.Min(amount * (asOfDate.DayNumber - dueDate.DayNumber), cap ?? decimal.MaxValue),
            _ => 0
        };
    }

    public static MoveInQuote CalculateMoveIn(decimal monthlyRent, DateOnly movedInAt)
    {
        EnsureNonNegative(monthlyRent, nameof(monthlyRent));
        var daysInPeriod = DateTime.DaysInMonth(movedInAt.Year, movedInAt.Month);
        var daysCharged = daysInPeriod - movedInAt.Day + 1;
        var proratedRent = decimal.Floor(monthlyRent * daysCharged / daysInPeriod);

        return new MoveInQuote(
            movedInAt,
            new DateOnly(movedInAt.Year, movedInAt.Month, daysInPeriod),
            (short)daysCharged,
            (byte)daysInPeriod,
            proratedRent,
            monthlyRent,
            proratedRent + monthlyRent);
    }

    public static InvoiceQuote CalculateInvoice(
        decimal monthlyRent,
        DateOnly movedInAt,
        int year,
        int month,
        decimal waterPrevious,
        decimal waterCurrent,
        decimal electricPrevious,
        decimal electricCurrent,
        decimal waterRate,
        decimal electricRate,
        decimal trashPerMonth,
        decimal adjustmentAmount = 0)
    {
        EnsureNonNegative(monthlyRent, nameof(monthlyRent));
        EnsureNonNegative(waterRate, nameof(waterRate));
        EnsureNonNegative(electricRate, nameof(electricRate));
        EnsureNonNegative(trashPerMonth, nameof(trashPerMonth));

        if (waterCurrent < waterPrevious)
            throw new BillingRuleException("เลขมิเตอร์น้ำปัจจุบันต้องไม่น้อยกว่าครั้งก่อน");
        if (electricCurrent < electricPrevious)
            throw new BillingRuleException("เลขมิเตอร์ไฟปัจจุบันต้องไม่น้อยกว่าครั้งก่อน");

        var periodStart = new DateOnly(year, month, 1);
        var daysInPeriod = DateTime.DaysInMonth(year, month);
        var periodEnd = new DateOnly(year, month, daysInPeriod);
        if (movedInAt > periodEnd)
            throw new BillingRuleException("วันย้ายเข้าต้องไม่อยู่หลังงวดบิล");

        var chargeStart = movedInAt > periodStart ? movedInAt : periodStart;
        var daysCharged = periodEnd.DayNumber - chargeStart.DayNumber + 1;
        var rent = decimal.Floor(monthlyRent * daysCharged / daysInPeriod);
        var waterUnits = waterCurrent - waterPrevious;
        var electricUnits = electricCurrent - electricPrevious;
        var trash = daysCharged == daysInPeriod ? trashPerMonth : 0;
        var waterAmount = waterUnits * waterRate;
        var electricAmount = electricUnits * electricRate;

        return new InvoiceQuote(
            periodStart,
            periodEnd,
            chargeStart,
            (short)daysCharged,
            (byte)daysInPeriod,
            rent,
            waterUnits,
            waterRate,
            waterAmount,
            electricUnits,
            electricRate,
            electricAmount,
            trash,
            adjustmentAmount,
            rent + waterAmount + electricAmount + trash + adjustmentAmount);
    }

    public static MoveOutQuote CalculateMoveOut(
        DateOnly movedInAt,
        DateOnly moveOutDate,
        byte minimumStayMonths,
        decimal depositAmount,
        decimal waterPrevious,
        decimal waterFinal,
        decimal electricPrevious,
        decimal electricFinal,
        decimal waterRate,
        decimal electricRate,
        decimal outstandingAmount,
        IReadOnlyCollection<SettlementDeductionQuote>? deductions = null)
    {
        if (moveOutDate < movedInAt)
            throw new BillingRuleException("วันย้ายออกต้องไม่อยู่ก่อนวันย้ายเข้า");
        if (waterFinal < waterPrevious)
            throw new BillingRuleException("เลขมิเตอร์น้ำวันย้ายออกต้องไม่น้อยกว่าครั้งก่อน");
        if (electricFinal < electricPrevious)
            throw new BillingRuleException("เลขมิเตอร์ไฟวันย้ายออกต้องไม่น้อยกว่าครั้งก่อน");

        foreach (var value in new[] { depositAmount, waterRate, electricRate, outstandingAmount })
            EnsureNonNegative(value, "amount");

        deductions ??= [];
        if (deductions.Any(x => x.Amount <= 0 || string.IsNullOrWhiteSpace(x.Description)))
            throw new BillingRuleException("ค่าเสียหายต้องมีรายละเอียดและจำนวนมากกว่า 0");

        var monthsStayed = decimal.Round(
            (moveOutDate.DayNumber - movedInAt.DayNumber) / AverageDaysPerMonth,
            2,
            MidpointRounding.AwayFromZero);
        var isForfeited = monthsStayed < minimumStayMonths - 0.5m;
        var waterAmount = (waterFinal - waterPrevious) * waterRate;
        var electricAmount = (electricFinal - electricPrevious) * electricRate;
        var deductionAmount = deductions.Sum(x => x.Amount);
        var totalDeducted = waterAmount + electricAmount + outstandingAmount + deductionAmount;
        var shortage = Math.Max(totalDeducted - depositAmount, 0);
        var remainingDeposit = Math.Max(depositAmount - totalDeducted, 0);

        return new MoveOutQuote(
            monthsStayed,
            isForfeited,
            isForfeited ? $"อยู่ไม่ครบ {minimumStayMonths} เดือน" : null,
            waterAmount,
            electricAmount,
            outstandingAmount,
            deductionAmount,
            totalDeducted,
            isForfeited ? 0 : remainingDeposit,
            shortage,
            isForfeited ? remainingDeposit : 0);
    }

    private static void EnsureNonNegative(decimal value, string name)
    {
        if (value < 0)
            throw new BillingRuleException($"{name} ต้องไม่ติดลบ");
    }
}

public sealed class BillingRuleException(string message) : Exception(message);

public sealed record MoveInQuote(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    short DaysCharged,
    byte DaysInPeriod,
    decimal RentAmount,
    decimal DepositAmount,
    decimal TotalDue);

public sealed record InvoiceQuote(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly ChargeStart,
    short DaysCharged,
    byte DaysInPeriod,
    decimal RentAmount,
    decimal WaterUnits,
    decimal WaterRate,
    decimal WaterAmount,
    decimal ElectricUnits,
    decimal ElectricRate,
    decimal ElectricAmount,
    decimal TrashAmount,
    decimal AdjustmentAmount,
    decimal TotalAmount);

public sealed record SettlementDeductionQuote(string Description, decimal Amount, string? PhotoUrl = null);

public sealed record MoveOutQuote(
    decimal MonthsStayed,
    bool IsForfeited,
    string? ForfeitReason,
    decimal FinalWaterAmount,
    decimal FinalElectricAmount,
    decimal OutstandingAmount,
    decimal DeductionAmount,
    decimal TotalDeducted,
    decimal RefundAmount,
    decimal AmountDueFromTenant,
    decimal ForfeitedAmount);
