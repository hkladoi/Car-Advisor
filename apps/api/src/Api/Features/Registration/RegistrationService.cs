using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Registration;

public interface IRegistrationService
{
    Task<RegionsResponse> GetRegionsAsync(CancellationToken cancellationToken);
    Task<OnRoadCalculationResponse> CalculateAsync(OnRoadCalculationRequest request, CancellationToken cancellationToken);
}

public sealed class RegistrationService(AppDbContext database, TimeProvider timeProvider) : IRegistrationService
{
    public async Task<RegionsResponse> GetRegionsAsync(CancellationToken cancellationToken)
    {
        var regions = await database.Regions.AsNoTracking()
            .Where(region => region.Active && region.Type == "Province")
            .OrderBy(region => region.Name)
            .ToListAsync(cancellationToken);
        var sources = await LoadSourcesAsync(regions.Select(region => region.SourceFactId), cancellationToken);
        return new RegionsResponse(
            regions.Select(region => new RegionItem(
                region.Code,
                region.Name,
                region.AreaClass ?? string.Empty,
                region.Type,
                Source(region.SourceFactId, sources))).ToArray(),
            timeProvider.GetUtcNow());
    }

    public async Task<OnRoadCalculationResponse> CalculateAsync(
        OnRoadCalculationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var instant = (request.CalculationDate ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var region = await database.Regions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Code == request.ProvinceCode && value.Active && value.Type == "Province", cancellationToken)
            ?? throw new RegistrationCalculationException(StatusCodes.Status400BadRequest, "UNKNOWN_PROVINCE", "ProvinceCode is not an active stable province code.");
        var car = await database.CurrentSearchableTrims.AsNoTracking()
            .SingleOrDefaultAsync(value => value.TrimId == request.TrimId, cancellationToken)
            ?? throw new RegistrationCalculationException(StatusCodes.Status404NotFound, "TRIM_NOT_FOUND", "The requested trim is not published in the Vietnam catalog.");

        var price = await database.Prices.AsNoTracking()
            .Where(value => value.TrimId == request.TrimId
                && value.Status == PriceStatus.Official
                && value.Amount != null
                && (value.PriceType == PriceType.PromotionPrice || value.PriceType == PriceType.Msrp)
                && value.EffectiveFrom <= instant
                && (value.EffectiveTo == null || value.EffectiveTo > instant)
                && (value.RegionScope == request.ProvinceCode || value.RegionScope == "VN"))
            .OrderByDescending(value => value.RegionScope == request.ProvinceCode)
            .ThenBy(value => value.PriceType == PriceType.PromotionPrice ? 0 : 1)
            .ThenBy(value => value.Priority)
            .ThenByDescending(value => value.Version)
            .ThenByDescending(value => value.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RegistrationCalculationException(StatusCodes.Status422UnprocessableEntity, "PRICE_UNKNOWN", "No effective official purchase price is available for this trim and date.");

        var context = new RegistrationRuleContext(
            region.Code,
            region.AreaClass ?? string.Empty,
            request.VehicleType,
            car.Seats,
            car.PowertrainType,
            request.BuyerType,
            request.FirstInspectionExempt,
            request.RoadUsageMonths,
            price.Amount!.Value,
            request.ScenarioAttributes);

        var warnings = new List<string>();
        var benefits = await EvaluateBenefitsAsync(request, car.BrandId, instant, context, warnings, cancellationToken);
        var benefitSummary = PurchaseBenefitEvaluator.Summarize(benefits);
        var effectiveCashPrice = Math.Max(0, price.Amount.Value - benefitSummary.CashPurchaseReduction);
        context = context with { EffectiveCashPurchasePrice = effectiveCashPrice };

        var rules = await database.RegistrationRules.AsNoTracking()
            .Where(rule => rule.EffectiveFrom <= instant && (rule.EffectiveTo == null || rule.EffectiveTo > instant))
            .ToListAsync(cancellationToken);
        var evaluated = RegistrationRuleEvaluator.Evaluate(rules, context, instant);
        if (car.Seats is null)
        {
            warnings.Add("SEATS_UNKNOWN: seat-dependent fees cannot be calculated until a sourced seat fact is available.");
        }

        foreach (var component in Enum.GetValues<RegistrationComponent>().Where(value => value != RegistrationComponent.Other))
        {
            if (evaluated.All(value => value.Rule.Component != component))
            {
                warnings.Add($"RULE_NOT_APPLICABLE: no effective {component} rule matched the supplied facts.");
            }
        }

        var sourceIds = evaluated.Select(value => value.Rule.SourceFactId)
            .Append(price.SourceFactId)
            .Append(region.SourceFactId);
        var sourceMap = await LoadSourcesAsync(sourceIds, cancellationToken);
        var appliedRules = evaluated.Select(value => AppliedRule(value.Rule, sourceMap)).ToArray();
        var breakdown = evaluated.Select(value =>
        {
            var support = value.Rule.Component switch
            {
                RegistrationComponent.PlateAndRegistrationFee => Math.Min(value.Amount, benefitSummary.RegistrationFeeSupport),
                RegistrationComponent.FirstRegistrationTax => Math.Min(value.Amount, benefitSummary.FirstRegistrationTaxSupport),
                _ => 0,
            };
            return new OnRoadBreakdownItem(
                value.Rule.Component.ToString(),
                value.Amount,
                support,
                value.Amount - support,
                AppliedRule(value.Rule, sourceMap));
        }).ToArray();
        var feeSupport = breakdown.Sum(item => item.EligibleSupport);
        var onRoad = effectiveCashPrice + breakdown.Sum(item => item.Amount);
        if (price.SourceFactId is null)
        {
            warnings.Add("PRICE_MANUAL_OVERRIDE: input price does not have a source fact.");
        }

        return new OnRoadCalculationResponse(
            new OnRoadResult(onRoad, effectiveCashPrice, price.Amount.Value, benefitSummary.CashPurchaseReduction, feeSupport, price.Currency),
            new VehicleCalculationIdentity(car.TrimId, car.BrandName, car.ModelName, car.TrimName, car.ModelYear, car.PowertrainType, car.Seats),
            new RegionItem(region.Code, region.Name, region.AreaClass ?? string.Empty, region.Type, Source(region.SourceFactId, sourceMap)),
            instant,
            new InputPriceReference(price.Id, price.PriceType.ToString(), price.Version, price.Amount.Value, price.Currency, price.RegionScope, price.EffectiveFrom, price.EffectiveTo, Source(price.SourceFactId, sourceMap)),
            breakdown,
            [
                $"Buyer type: {request.BuyerType}",
                $"Vehicle type: {request.VehicleType}",
                $"Road usage period: {request.RoadUsageMonths} months",
                $"Initial inspection exemption: {request.FirstInspectionExempt}",
            ],
            appliedRules,
            benefitSummary.CashBenefits.Concat(benefits.Where(value => value.Type is BenefitType.RegistrationFeeSupport or BenefitType.FirstRegistrationTaxSupport)).Select(ToContract).ToArray(),
            benefitSummary.NonCashBenefits.Select(ToContract).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            timeProvider.GetUtcNow());
    }

    private async Task<IReadOnlyList<PurchaseBenefit>> EvaluateBenefitsAsync(
        OnRoadCalculationRequest request,
        Guid brandId,
        DateTimeOffset instant,
        RegistrationRuleContext context,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var promotions = await database.Promotions.AsNoTracking()
            .Where(value => value.Status == OfferStatus.Published
                && (value.TrimId == request.TrimId || value.BrandId == brandId)
                && value.EffectiveFrom <= instant
                && (value.EffectiveTo == null || value.EffectiveTo > instant))
            .ToListAsync(cancellationToken);
        var benefits = promotions
            .Where(value => RegistrationRuleEvaluator.Matches(value.ConditionsJson, context))
            .Select(value => new PurchaseBenefit(
                value.BenefitType,
                value.Value,
                value.Value,
                value.BenefitType is BenefitType.CashDiscount or BenefitType.RegistrationFeeSupport or BenefitType.FirstRegistrationTaxSupport,
                "Promotion",
                value.Id))
            .ToList();

        var offers = await (
                from offer in database.DealerOffers.AsNoTracking()
                join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
                where offer.TrimId == request.TrimId
                    && offer.Status == OfferStatus.Published
                    && branch.ProvinceCode == request.ProvinceCode
                    && offer.EffectiveFrom <= instant
                    && (offer.EffectiveTo == null || offer.EffectiveTo > instant)
                select offer)
            .ToListAsync(cancellationToken);
        if (request.SelectedOfferIds.Count > 0)
        {
            var selected = request.SelectedOfferIds.ToHashSet();
            var missing = selected.Except(offers.Select(offer => offer.Id)).ToArray();
            if (missing.Length > 0)
            {
                warnings.Add("OFFER_INELIGIBLE: one or more selected offers are not active for this trim and province.");
            }
            offers = offers.Where(offer => selected.Contains(offer.Id)).ToList();
        }

        offers = offers.Where(offer => RegistrationRuleEvaluator.Matches(offer.ConditionsJson, context)).ToList();
        var offerIds = offers.Select(offer => offer.Id).ToArray();
        var offerBenefits = offerIds.Length == 0
            ? []
            : await database.DealerOfferBenefits.AsNoTracking()
                .Where(value => offerIds.Contains(value.OfferId))
                .ToListAsync(cancellationToken);
        var totals = offerBenefits.GroupBy(value => value.OfferId)
            .ToDictionary(group => group.Key, group => group.Where(value => value.IsCashEquivalent).Sum(value => value.CashValue ?? 0));
        var chosenOffers = offers
            .GroupBy(offer => string.IsNullOrWhiteSpace(offer.CombinabilityGroup) ? $"offer:{offer.Id}" : $"group:{offer.CombinabilityGroup}")
            .Select(group => group.OrderByDescending(offer => totals.GetValueOrDefault(offer.Id)).First())
            .ToArray();
        var chosenIds = chosenOffers.Select(offer => offer.Id).ToHashSet();
        var compatibleBenefits = offerBenefits.Where(value => chosenIds.Contains(value.OfferId));
        var selectedBenefits = compatibleBenefits
            .GroupBy(value => string.IsNullOrWhiteSpace(value.ExclusivityGroup) ? $"benefit:{value.Id}" : $"exclusive:{value.ExclusivityGroup}")
            .Select(group => group.OrderByDescending(value => value.CashValue ?? value.StatedValue ?? 0).First());
        benefits.AddRange(selectedBenefits.Select(value => new PurchaseBenefit(
            value.Type,
            value.CashValue,
            value.StatedValue,
            value.IsCashEquivalent,
            "DealerOffer",
            value.OfferId,
            value.Note)));
        return benefits;
    }

    private async Task<Dictionary<Guid, RuleSourceReference>> LoadSourcesAsync(
        IEnumerable<Guid?> factIds,
        CancellationToken cancellationToken)
    {
        var ids = factIds.Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where ids.Contains(fact.Id)
                select new RuleSourceReference(
                    fact.Id,
                    source.Id,
                    source.Name,
                    source.Url,
                    source.AuthorityLevel.ToString(),
                    source.ContentType.ToString(),
                    snapshot.FetchedAt,
                    snapshot.ContentHash,
                    fact.Status.ToString(),
                    fact.Confidence.ToString()))
            .ToDictionaryAsync(value => value.SourceFactId, cancellationToken);
    }

    private static AppliedRuleReference AppliedRule(RegistrationRule rule, IReadOnlyDictionary<Guid, RuleSourceReference> sources) =>
        new(rule.Id, rule.Component.ToString(), rule.Version, rule.Priority, rule.EffectiveFrom, rule.EffectiveTo, Source(rule.SourceFactId, sources));

    private static RuleSourceReference? Source(Guid? id, IReadOnlyDictionary<Guid, RuleSourceReference> sources) =>
        id is not null && sources.TryGetValue(id.Value, out var source) ? source : null;

    private static AppliedBenefit ToContract(PurchaseBenefit value) =>
        new(value.Type.ToString(), value.CashValue, value.StatedValue, value.IsCashEquivalent, value.Origin, value.OriginId, value.Note);

    private static void Validate(OnRoadCalculationRequest request)
    {
        if (request.TrimId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProvinceCode))
        {
            throw new RegistrationCalculationException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "TrimId and ProvinceCode are required.");
        }
        if (request.RoadUsageMonths is < 1 or > 36)
        {
            throw new RegistrationCalculationException(StatusCodes.Status400BadRequest, "INVALID_ROAD_USAGE_PERIOD", "RoadUsageMonths must be between 1 and 36.");
        }
        if (request.CalculationDate is { } date && (date.Year < 2020 || date.Year > 2100))
        {
            throw new RegistrationCalculationException(StatusCodes.Status400BadRequest, "INVALID_DATE", "CalculationDate must be between 2020 and 2100.");
        }
    }
}
