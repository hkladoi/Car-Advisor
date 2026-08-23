using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Energy;
using VietnamCarPlatform.Domain.Rules;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class EnergyCostEvaluatorTests
{
    private static readonly Guid FuelRateId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PublicRateId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid ProviderId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    [Fact]
    public void IceGoldenUsesEffectiveFuelPriceAndOfficialCombinedConsumption()
    {
        var result = EnergyCostEvaluator.Evaluate(
            Context(PowertrainType.Ice, fuel: 5.95m, electric: null),
            new EnergyRate(FuelRateId, EnergyType.E10Ron95III, "Bộ Công Thương", 22_668, 0, true),
            [],
            null,
            []);

        Assert.Equal(1_348_746, result.CurrentCost);
        Assert.Equal(result.CurrentCost, result.NormalizedCost);
        Assert.Equal(59.5m, result.FuelLitres);
    }

    [Fact]
    public void BevHomeGoldenAddsLossAndChargesOnlyMarginalEvnTiers()
    {
        var result = EnergyCostEvaluator.Evaluate(
            Context(PowertrainType.Bev, fuel: null, electric: 13.012m) with
            {
                HouseholdBaseKwh = 250,
                HomeChargingShare = 1,
            },
            null,
            EvnTiers(),
            null,
            []);

        Assert.Equal(513_409, result.CurrentCost);
        Assert.Equal(130.12m, result.BatteryEnergyKwh);
        Assert.Equal(2, result.Breakdown.Count(item => item.Component == "HomeChargingTier"));
        Assert.Equal(144.57777777777777777777777778m, result.GridEnergyKwh);
    }

    [Fact]
    public void BevPublicGoldenAppliesTieredPostChargeFeeAndSessionCap()
    {
        var result = EnergyCostEvaluator.Evaluate(
            Context(PowertrainType.Bev, fuel: null, electric: 17.7m) with
            {
                HomeChargingShare = 0,
                PublicSessions = 2,
                PostChargeMinutesPerSession = 130,
            },
            null,
            [],
            VGreenTariff(),
            []);

        Assert.Equal(1_178_740, result.NormalizedCost);
        Assert.Equal(420_000, result.Breakdown.Single(item => item.Component == "PostChargeServiceFee").NormalizedAmount);
    }

    [Fact]
    public void FreeChargingGoldenRespectsPersonalBuyerDateAndMonthlySessionCap()
    {
        var promotionId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var result = EnergyCostEvaluator.Evaluate(
            Context(PowertrainType.Bev, fuel: null, electric: 17.7m) with
            {
                HomeChargingShare = 0,
                PublicSessions = 8,
                SessionsUsedThisMonth = 2,
                CustomerType = "Personal",
                PurchaseDate = new DateOnly(2026, 2, 10),
                PromotionEligibilityConfirmed = true,
            },
            null,
            [],
            VGreenTariff(),
            [new PromotionRule(
                promotionId,
                ChargingPromotionBenefit.Free,
                null,
                "{\"customerTypes\":[\"Personal\"],\"purchaseDateRequired\":true,\"purchasedOnOrAfter\":\"2026-02-10\"}",
                "{\"maxSessionsPerCarPerMonth\":10}")]);

        Assert.Equal(0, result.CurrentCost);
        Assert.Equal(758_740, result.NormalizedCost);
        Assert.Equal(758_740, result.PromotionSavings);
        Assert.Contains(promotionId, result.AppliedPromotionIds);
    }

    [Fact]
    public void PhevGoldenKeepsChargeSustainingFuelAndElectricConsumptionSeparate()
    {
        var result = EnergyCostEvaluator.Evaluate(
            Context(PowertrainType.Phev, fuel: 4.72m, electric: 16.9m) with
            {
                EvShare = 0.6m,
                HomeChargingShare = 0.7m,
                HouseholdBaseKwh = 300,
                PublicSessions = 3,
            },
            new EnergyRate(FuelRateId, EnergyType.E10Ron95III, "Bộ Công Thương", 22_668, 0, true),
            EvnTiers(),
            VGreenTariff(),
            []);

        Assert.Equal(848_996, result.CurrentCost);
        Assert.Equal(18.88m, result.FuelLitres);
        Assert.Equal(101.4m, result.BatteryEnergyKwh);
        Assert.Contains(result.Assumptions, value => value.Contains("applied separately", StringComparison.Ordinal));
    }

    private static EnergyCostContext Context(
        PowertrainType powertrain,
        decimal? fuel,
        decimal? electric) => new(
        powertrain,
        1_000,
        fuel,
        electric,
        powertrain == PowertrainType.Phev ? 0.5m : powertrain == PowertrainType.Bev ? 1 : 0,
        powertrain == PowertrainType.Ice ? 0 : 1,
        0.9m,
        HomeChargingMode.EvnMarginalTiers,
        0,
        null,
        0,
        0,
        0,
        null,
        "Personal",
        null,
        false);

    private static IReadOnlyList<EnergyRate> EvnTiers() =>
    [
        Tier(0, 50, 1_984, 1),
        Tier(50, 100, 2_050, 2),
        Tier(100, 200, 2_380, 3),
        Tier(200, 300, 2_998, 4),
        Tier(300, 400, 3_350, 5),
        Tier(400, null, 3_460, 6),
    ];

    private static EnergyRate Tier(int from, int? to, decimal amount, int id) =>
        new(Guid.Parse($"20000000-0000-0000-0000-{id:000000000000}"), EnergyType.Electricity, "EVN", amount, 0.1m, false, from, to);

    private static PublicChargingRate VGreenTariff() => new(
        PublicRateId,
        ProviderId,
        3_858,
        0,
        "{\"tiers\":[{\"fromMinute\":11,\"toMinute\":60,\"amountPerMinute\":1000},{\"fromMinute\":61,\"toMinute\":120,\"amountPerMinute\":2000},{\"fromMinute\":121,\"toMinute\":null,\"amountPerMinute\":4000}]}",
        1_000_000,
        true);
}
