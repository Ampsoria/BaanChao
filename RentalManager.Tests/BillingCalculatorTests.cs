using System.Globalization;
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
    public void MoveIn_OnTheFirstOfTheMonth_IsNotProrated()
    {
        var result = BillingCalculator.CalculateInvoice(
            2_000m, new DateOnly(2026, 9, 1), 2026, 9,
            100, 100, 500, 500, 20, 12, 40);

        Assert.Equal(30, result.DaysCharged);
        Assert.Equal(30, result.DaysInPeriod);
        Assert.Equal(2_000m, result.RentAmount);
        Assert.Equal(40m, result.TrashAmount);
    }

    [Fact]
    public void MoveIn_OnTheLastDayOfTheMonth_ChargesASingleDay()
    {
        var result = BillingCalculator.CalculateMoveIn(2_000m, new DateOnly(2026, 9, 30));

        Assert.Equal(1, result.DaysCharged);
        Assert.Equal(30, result.DaysInPeriod);
        Assert.Equal(66m, result.RentAmount); // FLOOR(2000 × 1 / 30)
    }

    [Fact]
    public void MoveIn_InNonLeapFebruary_UsesTwentyEightDays()
    {
        var result = BillingCalculator.CalculateMoveIn(2_000m, new DateOnly(2026, 2, 15));

        Assert.Equal(14, result.DaysCharged);
        Assert.Equal(28, result.DaysInPeriod);
        Assert.Equal(1_000m, result.RentAmount);
    }

    [Fact]
    public void RateChange_DoesNotAlterAnInvoiceAlreadyCalculatedWithTheOldRate()
    {
        // บิลงวดเดิมคิดด้วยอัตราเก่า
        var september = BillingCalculator.CalculateInvoice(
            2_000m, new DateOnly(2026, 1, 1), 2026, 9,
            100, 105, 500, 530, waterRate: 20, electricRate: 12, trashPerMonth: 40);
        // เดือนถัดมาขึ้นราคา — ค่าที่ snapshot ไว้ในบิลเก่าต้องไม่เปลี่ยนตาม
        var october = BillingCalculator.CalculateInvoice(
            2_000m, new DateOnly(2026, 1, 1), 2026, 10,
            105, 110, 530, 560, waterRate: 25, electricRate: 15, trashPerMonth: 50);

        Assert.Equal(20m, september.WaterRate);
        Assert.Equal(100m, september.WaterAmount);
        Assert.Equal(2_500m, september.TotalAmount);

        Assert.Equal(25m, october.WaterRate);
        Assert.Equal(125m, october.WaterAmount);
        Assert.Equal(2_625m, october.TotalAmount);
    }

    [Fact]
    public void MoveOut_JustUnderFourAndAHalfMonths_IsForfeited()
    {
        // อยู่ 4 เดือน 10 วัน (130 วัน = 4.27 เดือน) ยังไม่ถึง 4.5 จึงริบ
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 11), 5,
            2_000, 100, 100, 500, 500, 20, 12, 0);

        Assert.Equal(4.27m, result.MonthsStayed);
        Assert.True(result.IsForfeited);
        Assert.Equal(0m, result.RefundAmount);
        Assert.Equal(2_000m, result.ForfeitedAmount);
    }

    [Fact]
    public void MoveOut_WhenForfeitedAndDeductionsExceedDeposit_StillCollectsTheShortfall()
    {
        // อยู่ 3 เดือน (ริบ) และค่าน้ำ-ค่าไฟงวดสุดท้ายเกินมัดจำ
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), 5,
            depositAmount: 2_000,
            waterPrevious: 100, waterFinal: 150,      // 50 × 20 = 1,000
            electricPrevious: 500, electricFinal: 650, // 150 × 12 = 1,800
            waterRate: 20, electricRate: 12, outstandingAmount: 0);

        Assert.True(result.IsForfeited);
        Assert.Equal(2_800m, result.TotalDeducted);
        Assert.Equal(800m, result.AmountDueFromTenant); // เก็บส่วนเกินได้แม้ริบ
        Assert.Equal(0m, result.RefundAmount);
        Assert.Equal(0m, result.ForfeitedAmount);       // ไม่เหลือมัดจำให้ริบ
    }

    [Theory]
    // ครบเกณฑ์/ไม่ครบ × หักน้อย/หักเกิน — ทั้งสี่แบบต้องมีตัวเลขบวกได้ไม่เกินหนึ่งช่อง
    [InlineData("2026-09-01", 100, 500)]
    [InlineData("2026-09-01", 300, 900)]
    [InlineData("2026-04-01", 100, 500)]
    [InlineData("2026-04-01", 300, 900)]
    public void RefundAndAmountDue_AreNeverBothPositive(string moveOut, int waterFinal, int electricFinal)
    {
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1),
            DateOnly.ParseExact(moveOut, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            5,
            2_000, 100, waterFinal, 500, electricFinal, 20, 12, 0);

        Assert.False(result.RefundAmount > 0 && result.AmountDueFromTenant > 0);
        Assert.True(result.RefundAmount >= 0 && result.AmountDueFromTenant >= 0);
    }

    [Fact]
    public void MoveOut_MidMonth_DoesNotChargeAnyAdditionalRent()
    {
        // ย้ายออก 10 ก.ย. — จ่ายค่าห้องล่วงหน้าไปแล้ววันที่ 1 จึงไม่คิดเพิ่มและไม่คืน
        var result = BillingCalculator.CalculateMoveOut(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 10), 5,
            2_000, 100, 104, 500, 525, 20, 12, outstandingAmount: 0);

        // ยอดที่หักมีแค่ค่าน้ำ 80 + ค่าไฟ 300 ไม่มีบรรทัดค่าเช่างวดสุดท้าย
        Assert.Equal(80m, result.FinalWaterAmount);
        Assert.Equal(300m, result.FinalElectricAmount);
        Assert.Equal(380m, result.TotalDeducted);
        Assert.Equal(1_620m, result.RefundAmount);
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
