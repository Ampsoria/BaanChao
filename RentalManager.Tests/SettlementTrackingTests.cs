using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RentalManager.Api.Controllers;
using RentalManager.Core.Entities;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Documents;
using RentalManager.Infrastructure.Storage;
using Xunit;

namespace RentalManager.Tests;

public sealed class SettlementTrackingTests
{
    [Fact]
    public async Task MarkAmountDueCollected_TracksMethodTimeAndAudit()
    {
        await using var db = CreateDatabase();
        var tenant = new Tenant
        {
            RoomId = 1,
            FullName = "ผู้เช่าเดิม",
            MovedInAt = new DateOnly(2025, 1, 1),
            MovedOutAt = new DateOnly(2026, 8, 31),
            DepositAmount = 1_800m
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var settlement = new MoveOutSettlement
        {
            TenantId = tenant.TenantId,
            MoveOutDate = new DateOnly(2026, 8, 31),
            DepositAmount = 1_800m,
            FinalWaterAmount = 300m,
            FinalElectricAmount = 2_000m,
            OutstandingAmount = 0,
            DeductionAmount = 0,
            MonthsStayed = 20,
            IsForfeited = false
        };
        SetAmountDue(settlement, 500m);
        db.MoveOutSettlements.Add(settlement);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).MarkAmountDueCollected(settlement.SettlementId,
            new RefundRequest("PromptPay"), TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(settlement.AmountDueCollectedAt);
        Assert.Equal("PromptPay", settlement.AmountDueCollectionMethod);
        Assert.Contains(await db.AuditLogs.ToListAsync(TestContext.Current.CancellationToken),
            x => x.EntityName == "MoveOutSettlement" && x.FieldName == "AmountDueCollected");
    }

    private static RentalDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RentalDbContext>()
            .UseInMemoryDatabase($"settlement-tracking-{Guid.NewGuid():N}").Options;
        var db = new RentalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static SettlementsController CreateController(RentalDbContext db)
    {
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions
        {
            SlipRoot = Path.Combine(Path.GetTempPath(), $"settlement-test-{Guid.NewGuid():N}")
        }));
        var controller = new SettlementsController(db, new ReceiptService(), storage);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "SettlementTest")], "Test"))
            }
        };
        return controller;
    }

    private static void SetAmountDue(MoveOutSettlement settlement, decimal amount) =>
        typeof(MoveOutSettlement).GetProperty(nameof(MoveOutSettlement.AmountDueFromTenant))!
            .SetValue(settlement, amount);
}
