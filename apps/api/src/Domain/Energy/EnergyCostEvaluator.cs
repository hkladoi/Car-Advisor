using System.Text.Json;
using System.Globalization;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Rules;

namespace VietnamCarPlatform.Domain.Energy;

public enum HomeChargingMode
{
    EvnMarginalTiers,
    CustomFixedRate,
}

public sealed record EnergyRate(
    Guid Id,
    EnergyType EnergyType,
    string Provider,
    decimal Amount,
    decimal TaxRate,
    bool TaxIncluded,
    int TierFromInclusive = 0,
    int? TierToInclusive = null);

public sealed record PublicChargingRate(
    Guid Id,
    Guid ProviderId,
    decimal AmountPerKwh,
    decimal AmountPerSession,
    string OverstayRulesJson,
    decimal? OverstayCapPerSession,
    bool TaxIncluded);

public sealed record PromotionRule(
    Guid Id,
    ChargingPromotionBenefit Benefit,
    decimal? BenefitValue,
    string EligibilityJson,
    string CapsJson);

public sealed record EnergyCostContext(
    PowertrainType Powertrain,
    decimal MonthlyKilometres,
    decimal? FuelLitresPer100Km,
    decimal? ElectricKwhPer100Km,
    decimal EvShare,
    decimal HomeChargingShare,
    decimal ChargingEfficiency,
    HomeChargingMode HomeMode,
    decimal HouseholdBaseKwh,
    decimal? CustomHomeAmountPerKwh,
    int PublicSessions,
    int SessionsUsedThisMonth,
    int PostChargeMinutesPerSession,
    string? ConnectorType,
    string CustomerType,
    DateOnly? PurchaseDate,
    bool PromotionEligibilityConfirmed);

public sealed record EnergyCostBreakdown(
    string Component,
    decimal Quantity,
    string Unit,
    decimal NormalizedAmount,
    decimal CurrentAmount,
    Guid? RateId,
    string Detail);

public sealed record EnergyCostEvaluation(
    decimal CurrentCost,
    decimal NormalizedCost,
    decimal PromotionSavings,
    decimal FuelLitres,
    decimal BatteryEnergyKwh,
    decimal GridEnergyKwh,
    IReadOnlyList<EnergyCostBreakdown> Breakdown,
    IReadOnlyList<Guid> AppliedPromotionIds,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Warnings);

public static class EnergyCostEvaluator
{
    public static EnergyCostEvaluation Evaluate(
        EnergyCostContext context,
        EnergyRate? fuelRate,
        IReadOnlyList<EnergyRate> homeTiers,
        PublicChargingRate? publicRate,
        IReadOnlyList<PromotionRule> promotions)
    {
        Validate(context);
        var breakdown = new List<EnergyCostBreakdown>();
        var assumptions = new List<string>();
        var warnings = new List<string>();
        var appliedPromotions = new List<Guid>();

        var iceShare = context.Powertrain switch
        {
            PowertrainType.Phev or PowertrainType.Erev => 1 - context.EvShare,
            PowertrainType.Bev => 0,
            _ => 1,
        };
        var evShare = context.Powertrain switch
        {
            PowertrainType.Phev or PowertrainType.Erev => context.EvShare,
            PowertrainType.Bev => 1,
            _ => 0,
        };

        var fuelLitres = context.MonthlyKilometres * iceShare / 100m * (context.FuelLitresPer100Km ?? 0);
        if (fuelLitres > 0)
        {
            if (fuelRate is null)
            {
                throw new InvalidOperationException("An effective fuel price is required for the ICE distance share.");
            }

            var fuelCost = TaxInclusiveAmount(fuelRate.Amount, fuelRate.TaxRate, fuelRate.TaxIncluded) * fuelLitres;
            breakdown.Add(new EnergyCostBreakdown(
                "Fuel",
                fuelLitres,
                "litre",
                fuelCost,
                fuelCost,
                fuelRate.Id,
                $"{fuelRate.Provider}; {fuelRate.EnergyType}"));
        }

        var batteryEnergy = context.MonthlyKilometres * evShare / 100m * (context.ElectricKwhPer100Km ?? 0);
        var gridEnergy = batteryEnergy == 0 ? 0 : batteryEnergy / context.ChargingEfficiency;
        var homeEnergy = gridEnergy * context.HomeChargingShare;
        var publicEnergy = gridEnergy - homeEnergy;

        if (homeEnergy > 0)
        {
            var home = context.HomeMode switch
            {
                HomeChargingMode.CustomFixedRate => EvaluateCustomHome(context, homeEnergy),
                _ => EvaluateMarginalHome(context, homeEnergy, homeTiers),
            };
            breakdown.AddRange(home);
        }

        decimal publicCharge = 0;
        decimal overstay = 0;
        if (publicEnergy > 0)
        {
            if (publicRate is null)
            {
                throw new InvalidOperationException("An effective public charging tariff is required for the public charging share.");
            }

            publicCharge = publicEnergy * publicRate.AmountPerKwh
                + context.PublicSessions * publicRate.AmountPerSession;
            overstay = EvaluateOverstay(
                publicRate.OverstayRulesJson,
                publicRate.OverstayCapPerSession,
                context.PublicSessions,
                context.PostChargeMinutesPerSession,
                context.ConnectorType);
            breakdown.Add(new EnergyCostBreakdown(
                "PublicCharging",
                publicEnergy,
                "kWh",
                publicCharge,
                publicCharge,
                publicRate.Id,
                $"{context.PublicSessions} session(s); tariff includes tax: {publicRate.TaxIncluded}"));
            if (overstay > 0)
            {
                breakdown.Add(new EnergyCostBreakdown(
                    "PostChargeServiceFee",
                    context.PostChargeMinutesPerSession * context.PublicSessions,
                    "minute",
                    overstay,
                    overstay,
                    publicRate.Id,
                    "Tiered post-charge fee; excluded from charging-energy promotions."));
            }
        }

        decimal promotionSavings = 0;
        foreach (var promotion in promotions)
        {
            if (publicCharge <= promotionSavings || !Eligible(promotion, context))
            {
                continue;
            }

            var remainingCharge = publicCharge - promotionSavings;
            var sessionFraction = EligibleSessionFraction(promotion, context);
            if (sessionFraction <= 0)
            {
                continue;
            }

            var discount = promotion.Benefit switch
            {
                ChargingPromotionBenefit.Free => publicCharge * sessionFraction,
                ChargingPromotionBenefit.PercentageDiscount => publicCharge * sessionFraction * (promotion.BenefitValue ?? 0) / 100m,
                ChargingPromotionBenefit.FixedDiscount => promotion.BenefitValue ?? 0,
                ChargingPromotionBenefit.KwhCredit => publicRate is null
                    ? 0
                    : Math.Min(publicEnergy, promotion.BenefitValue ?? 0) * publicRate.AmountPerKwh,
                ChargingPromotionBenefit.SessionCredit => publicRate is null || context.PublicSessions == 0
                    ? 0
                    : publicCharge * Math.Min(context.PublicSessions, decimal.ToInt32(promotion.BenefitValue ?? 0)) / context.PublicSessions,
                _ => 0,
            };
            discount = Math.Min(remainingCharge, Math.Max(0, discount));
            if (discount == 0)
            {
                continue;
            }

            promotionSavings += discount;
            appliedPromotions.Add(promotion.Id);
        }

        if (promotionSavings > 0)
        {
            var index = breakdown.FindIndex(item => item.Component == "PublicCharging");
            var item = breakdown[index];
            breakdown[index] = item with { CurrentAmount = item.NormalizedAmount - promotionSavings };
        }

        assumptions.Add($"Charging efficiency: {context.ChargingEfficiency:P1}; grid energy includes charging loss.");
        if (context.Powertrain is PowertrainType.Phev or PowertrainType.Erev)
        {
            assumptions.Add($"PHEV distance split: {context.EvShare:P0} electric and {iceShare:P0} fuel; consumption figures are applied separately, not as a weighted combined test result.");
        }
        if (context.HomeMode == HomeChargingMode.EvnMarginalTiers && homeEnergy > 0)
        {
            assumptions.Add($"Household base consumption is {context.HouseholdBaseKwh:0.###} kWh; only incremental EV grid energy is charged through marginal household tiers.");
        }
        if (context.PublicSessions == 0 && publicEnergy > 0)
        {
            warnings.Add("PUBLIC_SESSIONS_ZERO: public energy has no session or post-charge fee because PublicSessions is zero.");
        }

        var normalized = breakdown.Sum(item => item.NormalizedAmount);
        var current = breakdown.Sum(item => item.CurrentAmount);
        return new EnergyCostEvaluation(
            RoundVnd(current),
            RoundVnd(normalized),
            RoundVnd(promotionSavings),
            fuelLitres,
            batteryEnergy,
            gridEnergy,
            breakdown.Select(item => item with
            {
                NormalizedAmount = RoundVnd(item.NormalizedAmount),
                CurrentAmount = RoundVnd(item.CurrentAmount),
            }).ToArray(),
            appliedPromotions,
            assumptions,
            warnings);
    }

    private static IReadOnlyList<EnergyCostBreakdown> EvaluateCustomHome(EnergyCostContext context, decimal homeEnergy)
    {
        if (context.CustomHomeAmountPerKwh is null)
        {
            throw new InvalidOperationException("CustomHomeAmountPerKwh is required in custom fixed-rate mode.");
        }

        var amount = homeEnergy * context.CustomHomeAmountPerKwh.Value;
        return
        [
            new EnergyCostBreakdown(
                "HomeChargingCustom",
                homeEnergy,
                "kWh",
                amount,
                amount,
                null,
                "User-supplied all-in home/rental electricity rate."),
        ];
    }

    private static List<EnergyCostBreakdown> EvaluateMarginalHome(
        EnergyCostContext context,
        decimal homeEnergy,
        IReadOnlyList<EnergyRate> tiers)
    {
        if (tiers.Count == 0)
        {
            throw new InvalidOperationException("Effective household electricity tiers are required in EVN marginal mode.");
        }

        var start = context.HouseholdBaseKwh;
        var end = start + homeEnergy;
        var result = new List<EnergyCostBreakdown>();
        foreach (var tier in tiers.OrderBy(value => value.TierFromInclusive))
        {
            var tierEnd = tier.TierToInclusive ?? decimal.MaxValue;
            var quantity = Math.Max(0, Math.Min(end, tierEnd) - Math.Max(start, tier.TierFromInclusive));
            if (quantity == 0)
            {
                continue;
            }

            var allInRate = TaxInclusiveAmount(tier.Amount, tier.TaxRate, tier.TaxIncluded);
            var amount = quantity * allInRate;
            result.Add(new EnergyCostBreakdown(
                "HomeChargingTier",
                quantity,
                "kWh",
                amount,
                amount,
                tier.Id,
                $"Marginal tier {tier.TierFromInclusive:0}-{(tier.TierToInclusive?.ToString(CultureInfo.InvariantCulture) ?? "above")} kWh; base rate {tier.Amount:0.##}; tax {tier.TaxRate:P0}."));
        }

        var allocated = result.Sum(item => item.Quantity);
        if (Math.Abs(allocated - homeEnergy) > 0.000001m)
        {
            throw new InvalidOperationException("Household electricity tiers do not cover the requested marginal grid energy.");
        }
        return result;
    }

    private static decimal EvaluateOverstay(
        string rulesJson,
        decimal? capPerSession,
        int sessions,
        int minutesPerSession,
        string? connectorType)
    {
        if (sessions <= 0 || minutesPerSession <= 0 || string.IsNullOrWhiteSpace(rulesJson))
        {
            return 0;
        }

        using var document = JsonDocument.Parse(rulesJson);
        if (document.RootElement.TryGetProperty("excludedConnectorTypes", out var excluded)
            && excluded.ValueKind == JsonValueKind.Array
            && excluded.EnumerateArray().Any(value => string.Equals(value.GetString(), connectorType, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }
        if (!document.RootElement.TryGetProperty("tiers", out var tiers) || tiers.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        decimal perSession = 0;
        foreach (var tier in tiers.EnumerateArray())
        {
            var from = tier.GetProperty("fromMinute").GetInt32();
            var to = tier.TryGetProperty("toMinute", out var toElement) && toElement.ValueKind != JsonValueKind.Null
                ? toElement.GetInt32()
                : int.MaxValue;
            var price = tier.GetProperty("amountPerMinute").GetDecimal();
            var quantity = Math.Max(0, Math.Min(minutesPerSession, to) - from + 1);
            perSession += quantity * price;
        }
        if (capPerSession is not null)
        {
            perSession = Math.Min(perSession, capPerSession.Value);
        }
        return perSession * sessions;
    }

    private static bool Eligible(PromotionRule promotion, EnergyCostContext context)
    {
        using var document = JsonDocument.Parse(promotion.EligibilityJson);
        var root = document.RootElement;
        if (root.TryGetProperty("requiresEligibilityConfirmation", out var confirmation)
            && confirmation.ValueKind == JsonValueKind.True
            && !context.PromotionEligibilityConfirmed)
        {
            return false;
        }
        if (root.TryGetProperty("customerTypes", out var customerTypes)
            && customerTypes.ValueKind == JsonValueKind.Array
            && !customerTypes.EnumerateArray().Any(item => string.Equals(item.GetString(), context.CustomerType, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (root.TryGetProperty("purchaseDateRequired", out var required)
            && required.ValueKind == JsonValueKind.True
            && context.PurchaseDate is null)
        {
            return false;
        }
        if (root.TryGetProperty("purchasedOnOrAfter", out var after)
            && DateOnly.TryParse(after.GetString(), out var boundary)
            && (context.PurchaseDate is null || context.PurchaseDate < boundary))
        {
            return false;
        }
        return true;
    }

    private static decimal EligibleSessionFraction(PromotionRule promotion, EnergyCostContext context)
    {
        if (context.PublicSessions <= 0)
        {
            return 0;
        }
        using var document = JsonDocument.Parse(promotion.CapsJson);
        if (!document.RootElement.TryGetProperty("maxSessionsPerCarPerMonth", out var capElement))
        {
            return 1;
        }
        var remaining = Math.Max(0, capElement.GetInt32() - context.SessionsUsedThisMonth);
        return Math.Min(context.PublicSessions, remaining) / (decimal)context.PublicSessions;
    }

    private static decimal TaxInclusiveAmount(decimal amount, decimal taxRate, bool included) =>
        included ? amount : amount * (1 + taxRate);

    private static decimal RoundVnd(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    private static void Validate(EnergyCostContext context)
    {
        if (context.MonthlyKilometres < 0
            || context.FuelLitresPer100Km < 0
            || context.ElectricKwhPer100Km < 0
            || context.HouseholdBaseKwh < 0
            || context.CustomHomeAmountPerKwh < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Distance, consumption, base use, and rates cannot be negative.");
        }
        if (context.EvShare is < 0 or > 1 || context.HomeChargingShare is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "EV share and home charging share must be between zero and one.");
        }
        if (context.ChargingEfficiency is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Charging efficiency must be greater than zero and at most one.");
        }
        if (context.PublicSessions < 0 || context.SessionsUsedThisMonth < 0 || context.PostChargeMinutesPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Session counts and minutes cannot be negative.");
        }
        if (context.Powertrain is PowertrainType.Bev or PowertrainType.Phev or PowertrainType.Erev
            && context.ElectricKwhPer100Km is null)
        {
            throw new ArgumentException("Electric consumption is required for BEV/PHEV/E-REV calculations.", nameof(context));
        }
        if (context.Powertrain is not PowertrainType.Bev && context.FuelLitresPer100Km is null)
        {
            throw new ArgumentException("Fuel consumption is required for ICE/HEV/PHEV/E-REV calculations.", nameof(context));
        }
    }
}
