using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentalManager.Core.Entities;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Data;

namespace RentalManager.Infrastructure.Services;

public sealed partial class RentalOperationsService(RentalDbContext db, IOptions<BillingOptions> billingOptions)
{
    private readonly BillingOptions _billing = billingOptions.Value;

    /// <summary>
    /// งวดค่าน้ำ-ค่าไฟของบิล = เดือนก่อนงวดค่าเช่าเสมอ
    /// เพราะเดินจดมิเตอร์วันสิ้นเดือนแล้วออกบิลวันที่ 1 (CLAUDE.md ข้อ 4)
    /// </summary>
    public static string PreviousPeriod(string billingPeriod)
    {
        EnsureBillingPeriod(billingPeriod);
        var start = DateOnly.ParseExact(billingPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return start.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    public async Task<MoveInQuote> PreviewMoveInAsync(int roomId, DateOnly movedInAt, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking().SingleOrDefaultAsync(
            x => x.RoomId == roomId && x.IsActive, cancellationToken)
            ?? throw new RentalOperationException("ไม่พบห้องพักที่เปิดใช้งาน");
        return BillingCalculator.CalculateMoveIn(room.MonthlyRent, movedInAt);
    }

    public async Task<OperationResult<int>> MoveInAsync(MoveInCommand command, string changedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new RentalOperationException("กรุณาระบุชื่อผู้เช่า");
        if (command.Name.Trim().Length > 200 || command.Phone?.Trim().Length > 20)
            throw new RentalOperationException("ชื่อต้องยาวไม่เกิน 200 และเบอร์โทรไม่เกิน 20 ตัวอักษร");
        if (command.WaterReading < 0 || command.ElectricReading < 0)
            throw new RentalOperationException("เลขมิเตอร์ต้องไม่ติดลบ");
        if (!TenantChannels.IsValid(command.PreferredChannel))
            throw new RentalOperationException("ช่องทางรับบิลต้องเป็น Line หรือ Paper");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.RoomId == command.RoomId && x.IsActive, cancellationToken)
            ?? throw new RentalOperationException("ไม่พบห้องพักที่เปิดใช้งาน");
        var occupied = await db.Tenants.AnyAsync(x => x.RoomId == room.RoomId && x.MovedOutAt == null, cancellationToken);
        if (occupied)
            throw new RentalOperationException($"ห้อง {room.RoomNumber} มีผู้เช่าอยู่แล้ว");

        var quote = BillingCalculator.CalculateMoveIn(room.MonthlyRent, command.MovedInAt);
        var rate = await CurrentRateAsync(command.MovedInAt, cancellationToken);
        var period = command.MovedInAt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var tenant = new Tenant
        {
            RoomId = room.RoomId,
            FullName = command.Name.Trim(),
            Phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim(),
            MovedInAt = command.MovedInAt,
            DepositAmount = room.MonthlyRent,
            DepositReceivedAt = command.MovedInAt,
            MinimumStayMonths = _billing.MinimumStayMonths,
            PreferredChannel = command.PreferredChannel
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);

        db.MeterReadings.Add(new MeterReading
        {
            RoomId = room.RoomId,
            BillingPeriod = period,
            ReadAt = command.MovedInAt,
            WaterPrev = command.WaterReading,
            WaterCurrent = command.WaterReading,
            ElectricPrev = command.ElectricReading,
            ElectricCurrent = command.ElectricReading
        });
        db.Invoices.Add(new Invoice
        {
            RoomId = room.RoomId,
            TenantId = tenant.TenantId,
            BillingPeriod = period,
            // บิลใบแรกตอนย้ายเข้าไม่มีค่าน้ำ-ค่าไฟ เพราะเพิ่งจดเลขตั้งต้น
            UtilityPeriod = null,
            DueDate = command.MovedInAt,
            PeriodStart = quote.PeriodStart,
            PeriodEnd = quote.PeriodEnd,
            DaysCharged = quote.DaysCharged,
            DaysInPeriod = quote.DaysInPeriod,
            RentAmount = quote.RentAmount,
            WaterUnits = 0,
            WaterRate = rate.WaterPerUnit,
            ElectricUnits = 0,
            ElectricRate = rate.ElectricPerUnit,
            TrashAmount = 0
        });
        AddAudit("Tenant", tenant.TenantId.ToString(CultureInfo.InvariantCulture), "MoveIn", null,
            $"Room {room.RoomNumber}, {command.MovedInAt:yyyy-MM-dd}", changedBy);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OperationResult<int>(tenant.TenantId, $"ย้ายเข้าห้อง {room.RoomNumber} สำเร็จ ยอดวันส่งมอบ {quote.TotalDue:N2} บาท");
    }

    public async Task<OperationResult<int>> ImportExistingTenantAsync(
        ImportExistingTenantCommand command, string changedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new RentalOperationException("กรุณาระบุชื่อผู้เช่า");
        if (command.Name.Trim().Length > 200 || command.Phone?.Trim().Length > 20)
            throw new RentalOperationException("ชื่อต้องยาวไม่เกิน 200 และเบอร์โทรไม่เกิน 20 ตัวอักษร");
        if (command.DepositAmount < 0 || command.WaterReading < 0 || command.ElectricReading < 0)
            throw new RentalOperationException("มัดจำและเลขมิเตอร์ต้องไม่ติดลบ");
        if (command.MinimumStayMonths > 120)
            throw new RentalOperationException("ระยะพักขั้นต่ำต้องไม่เกิน 120 เดือน");
        EnsureBillingPeriod(command.MeterBillingPeriod);
        var meterPeriod = ParseBillingPeriod(command.MeterBillingPeriod);
        if (command.MeterReadAt.Year != meterPeriod.Year || command.MeterReadAt.Month != meterPeriod.Month)
            throw new RentalOperationException("วันที่จดมิเตอร์ต้องอยู่ในเดือนเดียวกับงวดมิเตอร์");
        if (command.MovedInAt > command.MeterReadAt)
            throw new RentalOperationException("วันเข้าอยู่ต้องไม่หลังวันที่จดมิเตอร์ตั้งต้น");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        if (command.MovedInAt > today || command.MeterReadAt > today)
            throw new RentalOperationException("ข้อมูลตั้งต้นต้องไม่เป็นวันที่ในอนาคต");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.RoomId == command.RoomId && x.IsActive, cancellationToken)
            ?? throw new RentalOperationException("ไม่พบห้องพักที่เปิดใช้งาน");
        if (await db.Tenants.AnyAsync(x => x.RoomId == command.RoomId && x.MovedOutAt == null, cancellationToken))
            throw new RentalOperationException($"ห้อง {room.RoomNumber} มีผู้เช่าอยู่แล้ว");
        var existingBaseline = await db.MeterReadings.SingleOrDefaultAsync(
            x => x.RoomId == command.RoomId && x.BillingPeriod == command.MeterBillingPeriod, cancellationToken);
        if (existingBaseline is not null &&
            (existingBaseline.WaterPrev != existingBaseline.WaterCurrent ||
             existingBaseline.ElectricPrev != existingBaseline.ElectricCurrent))
            throw new RentalOperationException(
                $"มิเตอร์งวด {command.MeterBillingPeriod} ของห้อง {room.RoomNumber} มีหน่วยใช้งานแล้ว จึงใช้เป็นเลขตั้งต้นไม่ได้");
        if (existingBaseline is not null &&
            (existingBaseline.ReadAt != command.MeterReadAt ||
             existingBaseline.WaterCurrent != command.WaterReading ||
             existingBaseline.ElectricCurrent != command.ElectricReading))
            throw new RentalOperationException(
                $"ห้อง {room.RoomNumber} มีเลขตั้งต้นงวด {command.MeterBillingPeriod} อยู่แล้ว: "
                + $"น้ำ {existingBaseline.WaterCurrent:N2} ไฟ {existingBaseline.ElectricCurrent:N2} วันที่ {existingBaseline.ReadAt:yyyy-MM-dd} "
                + "กรุณาใช้ค่าเดิมหรือแก้ในหน้าข้อมูลย้อนหลัง");

        var tenant = new Tenant
        {
            RoomId = command.RoomId,
            FullName = command.Name.Trim(),
            Phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim(),
            MovedInAt = command.MovedInAt,
            DepositAmount = command.DepositAmount,
            DepositReceivedAt = command.DepositAmount > 0
                ? command.DepositReceivedAt ?? command.MovedInAt
                : command.DepositReceivedAt,
            MinimumStayMonths = command.MinimumStayMonths,
            PreferredChannel = TenantChannels.Paper
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        if (existingBaseline is null)
            db.MeterReadings.Add(new MeterReading
            {
                RoomId = command.RoomId,
                BillingPeriod = command.MeterBillingPeriod,
                ReadAt = command.MeterReadAt,
                WaterPrev = command.WaterReading,
                WaterCurrent = command.WaterReading,
                ElectricPrev = command.ElectricReading,
                ElectricCurrent = command.ElectricReading
            });
        AddAudit("Tenant", tenant.TenantId.ToString(CultureInfo.InvariantCulture), "ImportExisting", null,
            $"Room {room.RoomNumber}, Baseline {command.MeterBillingPeriod}", changedBy);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OperationResult<int>(tenant.TenantId,
            existingBaseline is null
                ? $"นำเข้าผู้เช่าเดิมห้อง {room.RoomNumber} แล้ว โดยไม่สร้างบิลย้อนหลัง"
                : $"นำเข้าผู้เช่าเดิมห้อง {room.RoomNumber} แล้ว และใช้เลขมิเตอร์ตั้งต้นที่มีอยู่");
    }

    public async Task<OperationResult<int>> AddMeterReadingAsync(MeterReadingCommand command, string changedBy, CancellationToken cancellationToken)
    {
        EnsureBillingPeriod(command.BillingPeriod);
        if (command.WaterCurrent < 0 || command.ElectricCurrent < 0)
            throw new RentalOperationException("เลขมิเตอร์ต้องไม่ติดลบ");
        if (!await db.Rooms.AnyAsync(x => x.RoomId == command.RoomId && x.IsActive, cancellationToken))
            throw new RentalOperationException("ไม่พบห้องพักที่เปิดใช้งาน");
        var existingReading = await db.MeterReadings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RoomId == command.RoomId && x.BillingPeriod == command.BillingPeriod, cancellationToken);
        if (existingReading is not null)
            throw new RentalOperationException(
                $"ห้องนี้มีเลขมิเตอร์งวด {command.BillingPeriod} แล้ว (น้ำ {existingReading.WaterCurrent:N2} ไฟ {existingReading.ElectricCurrent:N2}) "
                + "ถ้าเป็นห้องที่เพิ่งย้ายเข้าเดือนนี้ ให้แก้เลขปัจจุบันของแถวเดิมแทนการเพิ่มแถวใหม่");

        var previous = await db.MeterReadings.AsNoTracking()
            .Where(x => x.RoomId == command.RoomId && x.ReadAt <= command.ReadAt)
            .OrderByDescending(x => x.ReadAt).ThenByDescending(x => x.ReadingId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RentalOperationException("ยังไม่มีเลขมิเตอร์ตั้งต้น กรุณาบันทึกการย้ายเข้าก่อน");
        if (command.WaterCurrent < previous.WaterCurrent || command.ElectricCurrent < previous.ElectricCurrent)
            throw new RentalOperationException("เลขมิเตอร์ใหม่ต้องไม่น้อยกว่าครั้งก่อน กรุณาตรวจสอบมิเตอร์วนรอบหรือการเปลี่ยนมิเตอร์");

        var reading = new MeterReading
        {
            RoomId = command.RoomId,
            BillingPeriod = command.BillingPeriod,
            ReadAt = command.ReadAt,
            WaterPrev = previous.WaterCurrent,
            WaterCurrent = command.WaterCurrent,
            ElectricPrev = previous.ElectricCurrent,
            ElectricCurrent = command.ElectricCurrent
        };
        db.MeterReadings.Add(reading);
        AddAudit("MeterReading", $"{command.RoomId}:{command.BillingPeriod}", "Create", null,
            $"Water={command.WaterCurrent}, Electric={command.ElectricCurrent}", changedBy);
        await db.SaveChangesAsync(cancellationToken);
        return new OperationResult<int>(reading.ReadingId, "บันทึกเลขมิเตอร์แล้ว");
    }

    public async Task<OperationResult<int>> GenerateMonthlyInvoicesAsync(string billingPeriod, string changedBy, CancellationToken cancellationToken)
    {
        var periodStart = ParseBillingPeriod(billingPeriod);
        var periodEnd = new DateOnly(periodStart.Year, periodStart.Month, DateTime.DaysInMonth(periodStart.Year, periodStart.Month));
        var rate = await CurrentRateAsync(periodStart, cancellationToken);
        var policy = await db.BillingPolicies.AsNoTracking()
            .Where(x => x.EffectiveFrom <= periodStart)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RentalOperationException("ไม่พบนโยบายการวางบิลที่มีผลในงวดนี้");
        var graceDays = policy.GraceDays > 0 ? policy.GraceDays : _billing.DueDay;
        var dueDay = Math.Min(graceDays, (byte)periodEnd.Day);
        var dueDate = new DateOnly(periodStart.Year, periodStart.Month, dueDay);

        // บิลที่ออกวันที่ 1 ต.ค. คิดค่าเช่าเดือน ต.ค. แต่ค่าน้ำ-ค่าไฟของเดือน ก.ย.
        var utilityPeriod = PreviousPeriod(billingPeriod);
        var readings = await db.MeterReadings.AsNoTracking()
            .Where(x => x.BillingPeriod == utilityPeriod)
            .ToDictionaryAsync(x => x.RoomId, cancellationToken);
        var tenants = await db.Tenants.Include(x => x.Room)
            .Where(x => x.Room.IsActive && x.MovedInAt <= periodEnd && (x.MovedOutAt == null || x.MovedOutAt >= periodStart))
            .ToListAsync(cancellationToken);
        var existing = await db.Invoices.AsNoTracking()
            .Where(x => x.BillingPeriod == billingPeriod && x.Status != InvoiceStatus.Void)
            .Select(x => new { x.RoomId, x.TenantId }).ToListAsync(cancellationToken);
        var existingKeys = existing.Select(x => (x.RoomId, x.TenantId)).ToHashSet();

        var created = 0;
        var withoutUtilities = new List<string>();
        foreach (var tenant in tenants)
        {
            // ออกบิลซ้ำงวดเดิมต้อง idempotent — ข้ามใบที่มีอยู่แล้วเงียบๆ (CLAUDE.md ข้อ 12)
            if (existingKeys.Contains((tenant.RoomId, tenant.TenantId)))
                continue;

            // คิดค่าน้ำ-ค่าไฟให้เฉพาะผู้เช่าที่อยู่มาตลอดงวดก่อนหน้าแล้ว
            // ผู้เช่าที่เพิ่งเข้าเดือนนี้ต้องไม่โดนหน่วยของคนเก่า (CLAUDE.md ข้อ 4)
            var occupiedUtilityPeriod = tenant.MovedInAt < periodStart;
            var meter = occupiedUtilityPeriod && readings.TryGetValue(tenant.RoomId, out var found) ? found : null;
            if (occupiedUtilityPeriod && meter is null)
            {
                withoutUtilities.Add(tenant.Room.RoomNumber);
                continue;
            }

            var quote = BillingCalculator.CalculateInvoice(
                tenant.Room.MonthlyRent, tenant.MovedInAt, periodStart.Year, periodStart.Month,
                meter?.WaterPrev ?? 0, meter?.WaterCurrent ?? 0,
                meter?.ElectricPrev ?? 0, meter?.ElectricCurrent ?? 0,
                rate.WaterPerUnit, rate.ElectricPerUnit, rate.TrashPerMonth);
            db.Invoices.Add(new Invoice
            {
                RoomId = tenant.RoomId,
                TenantId = tenant.TenantId,
                BillingPeriod = billingPeriod,
                UtilityPeriod = meter is null ? null : utilityPeriod,
                DueDate = dueDate,
                PeriodStart = quote.ChargeStart,
                PeriodEnd = quote.PeriodEnd,
                DaysCharged = quote.DaysCharged,
                DaysInPeriod = quote.DaysInPeriod,
                RentAmount = quote.RentAmount,
                WaterUnits = quote.WaterUnits,
                WaterRate = quote.WaterRate,
                ElectricUnits = quote.ElectricUnits,
                ElectricRate = quote.ElectricRate,
                TrashAmount = quote.TrashAmount
            });
            created++;
        }

        // เขียน audit เฉพาะตอนมีบิลเกิดจริง งานอัตโนมัติเรียกซ้ำทุกรอบเพื่อตามเก็บงวดที่ตกหล่น
        // ถ้าเขียนทุกครั้งจะกลายเป็น log ขยะวันละหลายร้อยแถว
        if (created > 0)
            AddAudit("Invoice", billingPeriod, "GenerateMonthly", null, created.ToString(CultureInfo.InvariantCulture), changedBy);
        await db.SaveChangesAsync(cancellationToken);

        // ห้ามเงียบ: บอกให้ชัดว่าห้องไหนออกบิลโดยยังไม่มีเลขมิเตอร์ของงวดก่อน
        var warning = withoutUtilities.Count == 0
            ? string.Empty
            : $" (ยังไม่ออกบิลห้อง {string.Join(", ", withoutUtilities)} เพราะไม่มีเลขมิเตอร์งวด {utilityPeriod})";
        return new OperationResult<int>(created,
            created == 0
                ? withoutUtilities.Count > 0
                    ? $"ยังไม่มีบิลใหม่{warning} กรุณาบันทึกมิเตอร์แล้วสั่งออกบิลอีกครั้ง"
                    : "ไม่มีบิลใหม่ (ทุกห้องในงวดนี้ออกบิลไปแล้ว)"
                : $"สร้างบิลใหม่ {created} ใบ{warning}");
    }

    public async Task<MoveOutQuote> PreviewMoveOutAsync(MoveOutCommand command, CancellationToken cancellationToken)
    {
        var context = await GetMoveOutContextAsync(command, cancellationToken);
        return CalculateMoveOut(command, context);
    }

    public async Task<OperationResult<int>> MoveOutAsync(MoveOutCommand command, string changedBy, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var context = await GetMoveOutContextAsync(command, cancellationToken, tracked: true);
        var quote = CalculateMoveOut(command, context);
        if (await db.MoveOutSettlements.AnyAsync(x => x.TenantId == command.TenantId, cancellationToken))
            throw new RentalOperationException("ผู้เช่ารายนี้มีใบสรุปย้ายออกแล้ว");

        var settlement = new MoveOutSettlement
        {
            TenantId = command.TenantId,
            MoveOutDate = command.MoveOutDate,
            DepositAmount = context.Tenant.DepositAmount,
            FinalWaterAmount = quote.FinalWaterAmount,
            FinalElectricAmount = quote.FinalElectricAmount,
            OutstandingAmount = quote.OutstandingAmount,
            DeductionAmount = quote.DeductionAmount,
            IsForfeited = quote.IsForfeited,
            ForfeitReason = quote.ForfeitReason,
            MonthsStayed = quote.MonthsStayed,
            Deductions = command.Deductions.Select(x => new SettlementDeduction
            {
                Description = x.Description.Trim(),
                Amount = x.Amount,
                PhotoUrl = x.PhotoUrl
            }).ToList()
        };
        context.Tenant.MovedOutAt = command.MoveOutDate;
        db.MoveOutSettlements.Add(settlement);
        AddAudit("Tenant", command.TenantId.ToString(CultureInfo.InvariantCulture), "MoveOut", null,
            command.MoveOutDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), changedBy);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OperationResult<int>(settlement.SettlementId,
            quote.AmountDueFromTenant > 0
                ? $"ย้ายออกสำเร็จ ต้องเก็บเพิ่ม {quote.AmountDueFromTenant:N2} บาท"
                : quote.IsForfeited
                    ? $"ย้ายออกสำเร็จ ริบมัดจำส่วนที่เหลือ {quote.ForfeitedAmount:N2} บาท"
                : $"ย้ายออกสำเร็จ คืนมัดจำ {quote.RefundAmount:N2} บาท");
    }

    private async Task<MoveOutContext> GetMoveOutContextAsync(MoveOutCommand command, CancellationToken cancellationToken, bool tracked = false)
    {
        var tenantQuery = tracked ? db.Tenants.AsQueryable() : db.Tenants.AsNoTracking();
        var tenant = await tenantQuery.SingleOrDefaultAsync(x => x.TenantId == command.TenantId, cancellationToken)
            ?? throw new RentalOperationException("ไม่พบผู้เช่า");
        if (tenant.MovedOutAt != null)
            throw new RentalOperationException("ผู้เช่ารายนี้ย้ายออกแล้ว");
        var rate = await CurrentRateAsync(command.MoveOutDate, cancellationToken);
        var meter = await db.MeterReadings.AsNoTracking().Where(x => x.RoomId == tenant.RoomId && x.ReadAt <= command.MoveOutDate)
            .OrderByDescending(x => x.ReadAt).ThenByDescending(x => x.ReadingId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RentalOperationException("ไม่พบเลขมิเตอร์ครั้งก่อน");
        var invoiceTotals = await db.Invoices.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.Status != InvoiceStatus.Void)
            .Select(x => new { x.InvoiceId, x.TotalAmount }).ToListAsync(cancellationToken);
        var invoiceIds = invoiceTotals.Select(x => x.InvoiceId).ToArray();
        var paid = await db.Payments.AsNoTracking().Where(x =>
                invoiceIds.Contains(x.InvoiceId) && x.VerificationStatus == "Verified")
            .GroupBy(x => x.InvoiceId).Select(x => new { InvoiceId = x.Key, Amount = x.Sum(y => y.PaidAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Amount, cancellationToken);
        var outstanding = invoiceTotals.Sum(x => Math.Max(x.TotalAmount - paid.GetValueOrDefault(x.InvoiceId), 0));
        return new MoveOutContext(tenant, rate, meter, outstanding);
    }

    private static MoveOutQuote CalculateMoveOut(MoveOutCommand command, MoveOutContext context) =>
        command.Deductions.Any(x => x.Description.Trim().Length > 200 || x.PhotoUrl?.Length > 500)
            ? throw new RentalOperationException("รายละเอียดค่าเสียหายต้องไม่เกิน 200 และ path รูปไม่เกิน 500 ตัวอักษร")
            : BillingCalculator.CalculateMoveOut(
            context.Tenant.MovedInAt, command.MoveOutDate, context.Tenant.MinimumStayMonths,
            context.Tenant.DepositAmount, context.Meter.WaterCurrent, command.WaterFinal,
            context.Meter.ElectricCurrent, command.ElectricFinal, context.Rate.WaterPerUnit,
            context.Rate.ElectricPerUnit, context.Outstanding, command.Deductions);

    private async Task<UtilityRate> CurrentRateAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.UtilityRates.AsNoTracking().Where(x => x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(cancellationToken)
        ?? throw new RentalOperationException("ไม่พบอัตราค่าบริการที่มีผลในวันที่เลือก");

    private void AddAudit(string entity, string key, string field, string? oldValue, string? newValue, string changedBy) =>
        db.AuditLogs.Add(new AuditLog
        {
            EntityName = entity,
            EntityKey = key,
            FieldName = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = changedBy
        });

    private static DateOnly ParseBillingPeriod(string value)
    {
        EnsureBillingPeriod(value);
        return DateOnly.ParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void EnsureBillingPeriod(string value)
    {
        if (!BillingPeriodRegex().IsMatch(value) ||
            !DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new RentalOperationException("งวดบิลต้องอยู่ในรูปแบบ YYYY-MM");
    }

    private sealed record MoveOutContext(Tenant Tenant, UtilityRate Rate, MeterReading Meter, decimal Outstanding);

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex BillingPeriodRegex();
}

public sealed class RentalOperationException(string message) : Exception(message);
