using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Pricing;

public interface IHistoryService
{
    Task<VehiclePriceHistoryResponse> GetVehiclePricesAsync(Guid trimId, VehiclePriceHistoryQuery queryOptions, CancellationToken cancellationToken);
    Task<DealerOfferHistoryResponse> GetDealerOffersAsync(Guid trimId, DealerOfferHistoryQuery queryOptions, CancellationToken cancellationToken);
    Task<EnergyPriceHistoryResponse> GetEnergyPricesAsync(EnergyPriceHistoryQuery queryOptions, CancellationToken cancellationToken);
}

public sealed partial class HistoryService(AppDbContext database, TimeProvider timeProvider) : IHistoryService
{
    private const int MaximumRows = 2_000;
    private static readonly TimeSpan DealerFreshness = TimeSpan.FromDays(14);
    private static readonly TimeSpan PromotionFreshness = TimeSpan.FromDays(7);

    public async Task<VehiclePriceHistoryResponse> GetVehiclePricesAsync(
        Guid trimId,
        VehiclePriceHistoryQuery queryOptions,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var months = ValidateMonths(queryOptions.Months);
        var scope = ValidateScope(queryOptions.RegionScope, "VN", "PRICE_REGION_INVALID");
        var cutoff = now.AddMonths(-months);
        var vehicle = await GetVehicleAsync(trimId, cancellationToken);

        var priceRows = await database.Prices.AsNoTracking()
            .Where(value => value.TrimId == trimId
                && (value.RegionScope == "VN" || value.RegionScope == scope)
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || value.EffectiveTo >= cutoff))
            .OrderByDescending(value => value.EffectiveFrom)
            .Take(MaximumRows + 1)
            .ToArrayAsync(cancellationToken);
        var historyRows = await database.PriceHistory.AsNoTracking()
            .Where(value => value.TrimId == trimId
                && (value.RegionScope == "VN" || value.RegionScope == scope)
                && value.EffectiveFrom <= now
                && (value.EffectiveTo ?? value.ArchivedAt) >= cutoff)
            .OrderByDescending(value => value.EffectiveFrom)
            .Take(MaximumRows + 1)
            .ToArrayAsync(cancellationToken);
        var promotionRows = await database.Promotions.AsNoTracking()
            .Where(value => (value.TrimId == trimId || value.BrandId == vehicle.BrandId)
                && (value.Status == OfferStatus.Published || value.Status == OfferStatus.Expired)
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || value.EffectiveTo >= cutoff))
            .OrderByDescending(value => value.EffectiveFrom)
            .Take(MaximumRows + 1)
            .ToArrayAsync(cancellationToken);
        var offerRows = await GetOfferRowsAsync(trimId, null, cutoff, now, cancellationToken);
        var offerBenefits = await LoadOfferBenefitsAsync(
            offerRows.Select(value => value.Offer.Id), cancellationToken);
        var factIds = priceRows.Select(value => value.SourceFactId)
            .Concat(historyRows.Select(value => value.SourceFactId))
            .Concat(promotionRows.Select(value => value.SourceFactId))
            .Concat(offerRows.Select(value => value.Offer.SourceFactId));
        var sources = await LoadSourcesAsync(factIds, cancellationToken);
        var timeline = new List<PriceTimelineEvent>();
        timeline.AddRange(priceRows.Take(MaximumRows).Select(value => PriceEvent(value, now, sources)));
        timeline.AddRange(historyRows.Take(MaximumRows).Select(value => PriceHistoryEvent(value, sources)));
        timeline.AddRange(promotionRows.Take(MaximumRows).Select(value => PromotionEvent(value, now, sources)));
        timeline.AddRange(offerRows.Take(MaximumRows).Select(value =>
            OfferEvent(value, offerBenefits[value.Offer.Id], now, sources)));
        var deduplicated = timeline
            .GroupBy(value => new
            {
                value.Series,
                value.ValueKind,
                value.Amount,
                value.Currency,
                value.Scope,
                value.Status,
                value.EffectiveFrom,
                value.EffectiveTo,
                SourceFactId = value.Source?.SourceFactId,
            })
            .Select(value => value.OrderByDescending(item => item.IsCurrent).First())
            .OrderBy(value => value.EffectiveFrom)
            .ThenBy(value => value.Series)
            .ToArray();
        var truncated = priceRows.Length > MaximumRows
            || historyRows.Length > MaximumRows
            || promotionRows.Length > MaximumRows
            || offerRows.Length > MaximumRows;
        return new VehiclePriceHistoryResponse(
            vehicle.Identity,
            deduplicated,
            EvaluateRange(deduplicated, now),
            new HistoryWindow(cutoff, now, months, truncated),
            now);
    }

    public async Task<DealerOfferHistoryResponse> GetDealerOffersAsync(
        Guid trimId,
        DealerOfferHistoryQuery queryOptions,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var months = ValidateMonths(queryOptions.Months);
        var province = string.IsNullOrWhiteSpace(queryOptions.ProvinceCode)
            ? null
            : ValidateScope(queryOptions.ProvinceCode, string.Empty, "DEALER_PROVINCE_INVALID");
        var cutoff = now.AddMonths(-months);
        var vehicle = await GetVehicleAsync(trimId, cancellationToken);
        var rows = await GetOfferRowsAsync(trimId, province, cutoff, now, cancellationToken);
        var benefits = await LoadOfferBenefitsAsync(rows.Select(value => value.Offer.Id), cancellationToken);
        var sources = await LoadSourcesAsync(rows.Select(value => value.Offer.SourceFactId), cancellationToken);
        var items = rows.Take(MaximumRows)
            .Select(value => MapOffer(value, benefits[value.Offer.Id], now, sources))
            .OrderByDescending(value => value.EffectiveFrom)
            .ToArray();
        return new DealerOfferHistoryResponse(
            vehicle.Identity,
            items.Where(value => value.IsCurrent).ToArray(),
            items.Where(value => !value.IsCurrent).ToArray(),
            "Only structured cash-equivalent benefits reduce purchase cash; stated non-cash values are never added to cash savings. Exclusive benefit groups contribute at most their largest eligible cash value.",
            new HistoryWindow(cutoff, now, months, rows.Length > MaximumRows),
            now);
    }

    public async Task<EnergyPriceHistoryResponse> GetEnergyPricesAsync(
        EnergyPriceHistoryQuery queryOptions,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var months = ValidateMonths(queryOptions.Months);
        var region = ValidateScope(queryOptions.RegionCode, "VN", "ENERGY_REGION_INVALID");
        var provider = ValidateProvider(queryOptions.Provider);
        EnergyType? energyType = null;
        if (!string.IsNullOrWhiteSpace(queryOptions.EnergyType))
        {
            if (!Enum.TryParse<EnergyType>(queryOptions.EnergyType, true, out var parsed))
            {
                throw Error("ENERGY_TYPE_INVALID", "EnergyType is not a supported canonical value.");
            }
            energyType = parsed;
        }
        var cutoff = now.AddMonths(-months);
        var query = database.EnergyPrices.AsNoTracking()
            .Where(value => value.RegionCode == region
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || value.EffectiveTo >= cutoff));
        if (energyType is not null) query = query.Where(value => value.EnergyType == energyType);
        if (provider is not null) query = query.Where(value => value.Provider == provider);
        var rows = await query
            .OrderByDescending(value => value.EffectiveFrom)
            .Take(MaximumRows + 1)
            .ToArrayAsync(cancellationToken);
        var sources = await LoadSourcesAsync(rows.Select(value => value.SourceFactId), cancellationToken);
        var series = rows.Take(MaximumRows)
            .GroupBy(value => new
            {
                value.EnergyType,
                value.Provider,
                value.RegionCode,
                value.Unit,
                value.Currency,
                value.TierFromInclusive,
                value.TierToInclusive,
            })
            .OrderBy(value => value.Key.EnergyType)
            .ThenBy(value => value.Key.Provider)
            .ThenBy(value => value.Key.TierFromInclusive)
            .Select(group => new EnergyPriceSeries(
                $"{group.Key.EnergyType}|{group.Key.Provider}|{group.Key.RegionCode}|{group.Key.TierFromInclusive}|{group.Key.TierToInclusive}",
                group.Key.EnergyType.ToString(),
                group.Key.Provider,
                group.Key.RegionCode,
                group.Key.Unit,
                group.Key.Currency,
                group.Key.TierFromInclusive,
                group.Key.TierToInclusive,
                group.OrderBy(value => value.EffectiveFrom)
                    .Select(value => new EnergyPriceObservation(
                        value.Id,
                        value.Amount,
                        value.TaxRate,
                        value.TaxIncluded,
                        value.EffectiveFrom,
                        value.EffectiveTo,
                        value.IsEffectiveAt(now),
                        Provenance(value.SourceFactId),
                        Source(value.SourceFactId, sources)))
                    .ToArray()))
            .ToArray();
        return new EnergyPriceHistoryResponse(
            series,
            new HistoryWindow(cutoff, now, months, rows.Length > MaximumRows),
            "Each series preserves energy type, provider, region, unit and electricity tier. Values from different series are never merged into one trend.",
            now);
    }

    public static CashPriceRangeInsight EvaluateRange(
        IReadOnlyList<PriceTimelineEvent> timeline,
        DateTimeOffset now)
    {
        const string policy = "At least three sourced/overridden official cash-price observations on three dates spanning 90 days within the trailing 12 months.";
        var cutoff = now.AddMonths(-12);
        var observations = timeline
            .Where(value => value.ValueKind == "CashPrice"
                && value.Status == PriceStatus.Official.ToString()
                && value.Amount is not null
                && value.EffectiveFrom >= cutoff
                && value.EffectiveFrom <= now)
            .GroupBy(value => new { Date = DateOnly.FromDateTime(value.EffectiveFrom.UtcDateTime), value.Amount, value.Series, value.Scope })
            .Select(value => value.First())
            .OrderBy(value => value.EffectiveFrom)
            .ToArray();
        var dates = observations.Select(value => DateOnly.FromDateTime(value.EffectiveFrom.UtcDateTime)).Distinct().ToArray();
        var spanDays = observations.Length < 2
            ? 0
            : (int)(observations[^1].EffectiveFrom - observations[0].EffectiveFrom).TotalDays;
        var current = observations.Where(value => value.IsCurrent).MinBy(value => value.Amount);
        if (current?.Amount is null)
        {
            return UnavailableRange("NO_CURRENT_CASH_PRICE", policy, observations.Length, dates.Length, spanDays);
        }
        if (observations.Length < 3 || dates.Length < 3 || spanDays < 90)
        {
            return UnavailableRange("INSUFFICIENT_12_MONTH_HISTORY", policy, observations.Length, dates.Length, spanDays, current.Amount, current.Currency);
        }
        var minimum = observations.Min(value => value.Amount!.Value);
        var maximum = observations.Max(value => value.Amount!.Value);
        var position = Position(current.Amount.Value, minimum, maximum);
        return new CashPriceRangeInsight(
            true,
            "Observed official MSRP/manufacturer-promotion/dealer-cash prices; benefit values and dealer quotes excluded.",
            policy,
            "ENOUGH_HISTORY",
            observations.Length,
            dates.Length,
            spanDays,
            current.Amount,
            minimum,
            maximum,
            current.Currency,
            position);
    }

    public static decimal? MaximumEligibleCashReduction(IEnumerable<DealerOfferBenefit> benefits)
    {
        var cash = benefits.Where(value => value.IsCashEquivalent && value.CashValue is not null).ToArray();
        if (cash.Length == 0) return null;
        var ungrouped = cash.Where(value => string.IsNullOrWhiteSpace(value.ExclusivityGroup)).Sum(value => value.CashValue!.Value);
        var exclusive = cash.Where(value => !string.IsNullOrWhiteSpace(value.ExclusivityGroup))
            .GroupBy(value => value.ExclusivityGroup!, StringComparer.OrdinalIgnoreCase)
            .Sum(group => group.Max(value => value.CashValue!.Value));
        return ungrouped + exclusive;
    }

    private async Task<VehicleRow> GetVehicleAsync(Guid trimId, CancellationToken cancellationToken) =>
        await (
                from trim in database.Trims.AsNoTracking()
                join modelYear in database.ModelYears.AsNoTracking() on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations.AsNoTracking() on modelYear.GenerationId equals generation.Id
                join model in database.Models.AsNoTracking() on generation.ModelId equals model.Id
                join brand in database.Brands.AsNoTracking() on model.BrandId equals brand.Id
                where trim.Id == trimId
                select new VehicleRow(
                    brand.Id,
                    new VehicleHistoryIdentity(trim.Id, brand.Name, model.Name, trim.Name, modelYear.Year)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new HistoryOperationException(404, "HISTORY_TRIM_NOT_FOUND", "The requested Vietnam-market trim was not found.");

    private async Task<OfferRow[]> GetOfferRowsAsync(
        Guid trimId,
        string? province,
        DateTimeOffset cutoff,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var query =
            from offer in database.DealerOffers.AsNoTracking()
            join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
            join dealer in database.Dealers.AsNoTracking() on branch.DealerId equals dealer.Id
            where offer.TrimId == trimId
                && (offer.Status == OfferStatus.Published || offer.Status == OfferStatus.Expired)
                && offer.EffectiveFrom <= now
                && (offer.EffectiveTo == null || offer.EffectiveTo >= cutoff)
                && (province == null || branch.ProvinceCode == province)
            orderby offer.EffectiveFrom descending
            select new OfferRow(offer, branch, dealer);
        return await query.Take(MaximumRows + 1).ToArrayAsync(cancellationToken);
    }

    private async Task<ILookup<Guid, DealerOfferBenefit>> LoadOfferBenefitsAsync(
        IEnumerable<Guid> offerIds,
        CancellationToken cancellationToken)
    {
        var ids = offerIds.Distinct().ToArray();
        var rows = await database.DealerOfferBenefits.AsNoTracking()
            .Where(value => ids.Contains(value.OfferId))
            .OrderBy(value => value.CreatedAt)
            .ToArrayAsync(cancellationToken);
        return rows.ToLookup(value => value.OfferId);
    }

    private async Task<Dictionary<Guid, HistorySourceReference>> LoadSourcesAsync(
        IEnumerable<Guid?> sourceFactIds,
        CancellationToken cancellationToken)
    {
        var ids = sourceFactIds.Where(value => value is not null).Select(value => value!.Value).Distinct().ToArray();
        return await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where ids.Contains(fact.Id)
                select new HistorySourceReference(
                    fact.Id,
                    source.Id,
                    source.Name,
                    source.Url,
                    source.AuthorityLevel.ToString(),
                    snapshot.FetchedAt,
                    snapshot.ContentHash,
                    fact.Confidence.ToString()))
            .ToDictionaryAsync(value => value.SourceFactId, cancellationToken);
    }

    private static PriceTimelineEvent PriceEvent(
        Price value,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, HistorySourceReference> sources) =>
        new(
            value.Id,
            PriceSeries(value.PriceType),
            PriceValueKind(value.PriceType),
            value.Amount,
            value.Currency,
            value.Status.ToString(),
            value.RegionScope,
            PriceLabel(value.PriceType),
            value.EffectiveFrom,
            value.EffectiveTo,
            (value.Status is PriceStatus.Official or PriceStatus.Expected) && value.IsEffectiveAt(now),
            false,
            Provenance(value.SourceFactId),
            Source(value.SourceFactId, sources));

    private static PriceTimelineEvent PriceHistoryEvent(
        PriceHistory value,
        IReadOnlyDictionary<Guid, HistorySourceReference> sources) =>
        new(
            value.Id,
            PriceSeries(value.PriceType),
            PriceValueKind(value.PriceType),
            value.Amount,
            value.Currency,
            value.Status.ToString(),
            value.RegionScope,
            PriceLabel(value.PriceType),
            value.EffectiveFrom,
            value.EffectiveTo ?? value.ArchivedAt,
            false,
            false,
            Provenance(value.SourceFactId),
            Source(value.SourceFactId, sources));

    private static PriceTimelineEvent PromotionEvent(
        Promotion value,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, HistorySourceReference> sources)
    {
        var source = Source(value.SourceFactId, sources);
        var verifiedAt = source?.FetchedAt ?? value.UpdatedAt;
        var stale = now - verifiedAt > PromotionFreshness;
        return new PriceTimelineEvent(
            value.Id,
            "ManufacturerPromotion",
            value.BenefitType == BenefitType.CashDiscount ? "CashBenefit" : "BenefitValue",
            value.Value,
            value.Currency,
            value.Status.ToString(),
            value.TrimId is null ? "Brand" : "Trim",
            value.BenefitType.ToString(),
            value.EffectiveFrom,
            value.EffectiveTo,
            value.Status == OfferStatus.Published && value.IsEffectiveAt(now) && !stale,
            stale,
            Provenance(value.SourceFactId),
            source);
    }

    private static PriceTimelineEvent OfferEvent(
        OfferRow row,
        IEnumerable<DealerOfferBenefit> benefits,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, HistorySourceReference> sources)
    {
        var benefitRows = benefits.ToArray();
        var source = Source(row.Offer.SourceFactId, sources);
        var verifiedAt = source?.FetchedAt ?? row.Offer.UpdatedAt;
        var stale = now - verifiedAt > DealerFreshness;
        return new PriceTimelineEvent(
            row.Offer.Id,
            "DealerCashOffer",
            "CashBenefit",
            MaximumEligibleCashReduction(benefitRows),
            benefitRows.Select(value => value.Currency).FirstOrDefault() ?? "VND",
            row.Offer.Status.ToString(),
            row.Branch.ProvinceCode,
            $"{row.Dealer.Name} · {row.Offer.Headline}",
            row.Offer.EffectiveFrom,
            row.Offer.EffectiveTo,
            row.Offer.Status == OfferStatus.Published && row.Offer.IsEffectiveAt(now) && !stale,
            stale,
            Provenance(row.Offer.SourceFactId),
            source);
    }

    private static DealerOfferHistoryItem MapOffer(
        OfferRow row,
        IEnumerable<DealerOfferBenefit> benefits,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, HistorySourceReference> sources)
    {
        var benefitRows = benefits.ToArray();
        var source = Source(row.Offer.SourceFactId, sources);
        var verifiedAt = source?.FetchedAt ?? row.Offer.UpdatedAt;
        var stale = now - verifiedAt > DealerFreshness;
        var currency = benefitRows.Select(value => value.Currency).FirstOrDefault() ?? "VND";
        return new DealerOfferHistoryItem(
            row.Offer.Id,
            row.Dealer.Name,
            row.Branch.Name,
            row.Branch.ProvinceCode,
            row.Offer.Headline,
            row.Offer.Status.ToString(),
            row.Offer.ConditionsJson,
            row.Offer.CombinabilityGroup,
            MaximumEligibleCashReduction(benefitRows),
            currency,
            row.Offer.EffectiveFrom,
            row.Offer.EffectiveTo,
            verifiedAt,
            row.Offer.Status == OfferStatus.Published && row.Offer.IsEffectiveAt(now) && !stale,
            stale,
            benefitRows.Select(value => new DealerOfferBenefitHistory(
                value.Type.ToString(),
                value.CashValue,
                value.StatedValue,
                value.Currency,
                value.IsCashEquivalent,
                value.ExclusivityGroup,
                value.Note)).ToArray(),
            Provenance(row.Offer.SourceFactId),
            source);
    }

    private static CashPriceRangeInsight UnavailableRange(
        string reason,
        string policy,
        int observations,
        int dates,
        int spanDays,
        decimal? current = null,
        string currency = "VND") => new(
            false,
            "Observed official MSRP/manufacturer-promotion/dealer-cash prices; benefit values and dealer quotes excluded.",
            policy,
            reason,
            observations,
            dates,
            spanDays,
            current,
            null,
            null,
            currency,
            null);

    private static string Position(decimal current, decimal minimum, decimal maximum)
    {
        if (minimum == maximum) return "Flat";
        if (current == minimum) return "At12MonthLow";
        if (current == maximum) return "At12MonthHigh";
        var ratio = (current - minimum) / (maximum - minimum);
        return ratio <= 0.25m ? "Near12MonthLow" : ratio >= 0.75m ? "Near12MonthHigh" : "MidRange";
    }

    private static int ValidateMonths(int? value)
    {
        var months = value ?? 12;
        if (months is < 1 or > 60)
        {
            throw Error("HISTORY_MONTHS_INVALID", "Months must be between 1 and 60.");
        }
        return months;
    }

    private static string ValidateScope(string? value, string fallback, string code)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
        if (!ScopePattern().IsMatch(normalized))
        {
            throw Error(code, "Region/province scope must be a 2-20 character canonical code.");
        }
        return normalized;
    }

    private static string? ValidateProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 200 || normalized.Any(char.IsControl))
        {
            throw Error("ENERGY_PROVIDER_INVALID", "Provider must contain at most 200 printable characters.");
        }
        return normalized;
    }

    private static string PriceSeries(PriceType value) => value switch
    {
        PriceType.Msrp => "Msrp",
        PriceType.PromotionPrice => "ManufacturerPromotionPrice",
        PriceType.DealerCashPrice => "DealerCashPrice",
        PriceType.DealerQuote => "DealerQuote",
        PriceType.ExpectedPrice => "ExpectedPrice",
        _ => "Unannounced",
    };

    private static string PriceValueKind(PriceType value) => value switch
    {
        PriceType.Msrp or PriceType.PromotionPrice or PriceType.DealerCashPrice => "CashPrice",
        PriceType.DealerQuote => "QuotedPrice",
        PriceType.ExpectedPrice => "ExpectedPrice",
        _ => "Unknown",
    };

    private static string PriceLabel(PriceType value) => value switch
    {
        PriceType.Msrp => "Official MSRP",
        PriceType.PromotionPrice => "Manufacturer promotion cash price",
        PriceType.DealerCashPrice => "Dealer cash price",
        PriceType.DealerQuote => "Dated dealer quote",
        PriceType.ExpectedPrice => "Expected price",
        _ => "Price unannounced",
    };

    private static string Provenance(Guid? sourceFactId) => sourceFactId is null ? "ManualOverride" : "SourceFact";

    private static HistorySourceReference? Source(
        Guid? sourceFactId,
        IReadOnlyDictionary<Guid, HistorySourceReference> sources) =>
        sourceFactId is { } id && sources.TryGetValue(id, out var source) ? source : null;

    private static HistoryOperationException Error(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    [GeneratedRegex("^[A-Z0-9][A-Z0-9:_-]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern();

    private sealed record VehicleRow(Guid BrandId, VehicleHistoryIdentity Identity);
    private sealed record OfferRow(DealerOffer Offer, DealerBranch Branch, Dealer Dealer);
}
