using RentalManager.Core.Services;
using Xunit;
using RentalManager.Core.Entities;

namespace RentalManager.Tests;

public sealed class BillingCalculatorTests
{
    [Theory]
    [InlineData(LateFeeType.None, 10, 100, 0)]
    [InlineData(LateFeeType.Flat, 50, 100, 50)]
    [InlineData(LateFeeType.PerDay, 10, 25, 25)]
    public void LateFee_UsesPolicyWithoutCompounding(LateFeeType type, decimal amount, decimal cap, decimal expected)
    {
        var result = BillingCalculator.CalculateLateFee(
            type, amount, cap, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 10));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MoveIn_OnSeptember17_MatchesDocumentExample()
    {
        var result = BillingCalculator.CalculateMoveIn(2_000m, new DateOnly(2026, 9, 17));

        Assert.Equal(14, result.DaysCharged);
        Assert.Equal(30, result.DaysInPeriod);
        Assert.Equal(933m, result.RentAmount);
        Assert.Equal(2_000m, result.DepositAmount);
        Assert.Equal(2_933m, result.TotalDue);
    }

    [Fact]
    public void MoveIn_UsesActualDaysInLeapFebruary_AndFloorsRent()
    {
        var result = BillingCalculator.CalculateMoveIn(2_000m, new DateOnly(2028, 2, 16));

        Assert.Equal(14, result.DaysCharged);
        Assert.Equal(29, result.DaysInPeriod);
        Assert.Equal(965m, result.RentAmount);
    }

    [Fact]
    public void MonthlyInvoice_SnapshotsRatesAndCalculatesEveryLine()
    {
        var result = BillingCalculator.CalculateInvoice(
            monthlyRent: 2_000m,
            movedInAt: new DateOnly(2026, 1, 1),
            year: 2026,
            month: 9,
            waterPrevious: 100,
            waterCurrent: 104,
            electricPrevious: 500,
            electricCurrent: 530,
            waterRate: 20,
            electricRate: 12,
            trashPerMonth: 40);

        Assert.Equal(2_000m, result.RentAmount);
        Assert.Equal(4m, result.WaterUnits);
        Assert.Equal(80m, result.WaterAmount);
        Assert.Equal(30m, result.ElectricUnits);
        Assert.Equal(360m, result.ElectricAmount);
        Assert.Equal(40m, result.TrashAmount);
        Assert.Equal(2_480m, result.TotalAmount);
    }

    [Fact]
    public void MonthlyInvoice_FirstPartialMonth_DoesNotChargeTrash()
    {
        var result = BillingCalculator.CalculateInvoice(
            2_000m, new DateOnly(2026, 9, 17), 2026, 9,
            10, 10, 20, 20, 20, 12, 40);

        Assert.Equal(933m, result.RentAmount);
        Assert.Equal(0m, result.TrashAmount);
        Assert.Equal(933m, result.TotalAmount);
    }

    [Fact]
    public void MeterRollover_IsRejectedInsteadOfSilentlyBillingNegativeUnits()
    {
        var exception = Assert.Throws<BillingRuleException>(() => BillingCalculator.CalculateInvoice(
            2_000m, new DateOnly(2026, 1, 1), 2026, 9,
            100, 99, 500, 510, 20, 12, 40));

        Assert.Contains("มิเตอร์น้ำ", exception.Message);
    }

    [Fact]
    public void MoveOut_AfterEightMonths_RefundsDepositRemainder()
    {
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 1), 5,
            2_000, 100, 104, 500, 525, 20, 12, 0);

        Assert.False(result.IsForfeited);
        Assert.Equal(1_620m, result.RefundAmount);
        Assert.Equal(0m, result.AmountDueFromTenant);
        Assert.Equal(0m, result.ForfeitedAmount);
    }

    [Fact]
    public void MoveOut_BeforeMinimumStay_ForfeitsRemainder()
    {
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), 5,
            2_000, 100, 104, 500, 525, 20, 12, 0);

        Assert.True(result.IsForfeited);
        Assert.Equal(0m, result.RefundAmount);
        Assert.Equal(1_620m, result.ForfeitedAmount);
    }

    [Fact]
    public void MoveOut_AtFourAndAHalfMonths_IsNotForfeited()
    {
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 19), 5,
            2_000, 100, 100, 500, 500, 20, 12, 0);

        Assert.Equal(4.53m, result.MonthsStayed);
        Assert.False(result.IsForfeited);
        Assert.Equal(2_000m, result.RefundAmount);
    }

    [Fact]
    public void MoveOut_WhenDeductionsExceedDeposit_ReturnsPositiveAmountDueOnly()
    {
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 1), 5,
            1_800, 100, 110, 500, 600, 20, 12, 600,
            [new SettlementDeductionQuote("กุญแจหาย", 100)]);

        Assert.Equal(300m, result.AmountDueFromTenant);
        Assert.Equal(0m, result.RefundAmount);
        Assert.Equal(0m, result.ForfeitedAmount);
    }
}
