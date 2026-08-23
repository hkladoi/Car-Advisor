using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminQualityService
{
    Task<AdminCoverageResponse> GetCoverageAsync(CancellationToken cancellationToken);
    Task<AdminQualityResponse> GetQualityAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminAuditResponse>> GetAuditAsync(int take, CancellationToken cancellationToken);
}

public sealed class AdminQualityService(AppDbContext database, TimeProvider timeProvider) : IAdminQualityService
{
    private static readonly string[] CoreSpecCodes = ["SEATS", "LENGTH_MM", "WIDTH_MM", "HEIGHT_MM", "WHEELBASE_MM"];

    public async Task<AdminCoverageResponse> GetCoverageAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var brands = await database.Brands.AsNoTracking().OrderBy(value => value.Name).ToArrayAsync(cancellationToken);
        var scopes = await database.BrandScopes.AsNoTracking()
            .Where(value => value.EffectiveFrom <= now && (value.EffectiveTo == null || value.EffectiveTo > now))
            .OrderByDescending(value => value.EffectiveFrom)
            .ToArrayAsync(cancellationToken);
        var trimRows = await (
                from trim in database.Trims.AsNoTracking()
                join modelYear in database.ModelYears.AsNoTracking() on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations.AsNoTracking() on modelYear.GenerationId equals generation.Id
                join model in database.Models.AsNoTracking() on generation.ModelId equals model.Id
                select new QualityTrimRow(trim, model.Id, model.BrandId, generation.Code, modelYear.Year))
            .ToArrayAsync(cancellationToken);
        var activeRows = trimRows.Where(value => value.Trim.MarketStatus is MarketStatus.Active or MarketStatus.Upcoming or MarketStatus.Announced).ToArray();
        var activeIds = activeRows.Select(value => value.Trim.Id).ToArray();
        var currentPrices = await database.Prices.AsNoTracking()
            .Where(value => activeIds.Contains(value.TrimId)
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || value.EffectiveTo > now)
                && value.RegionScope == "VN"
                && ((value.PriceType == PriceType.Msrp && (value.Status == PriceStatus.Official || value.Status == PriceStatus.Expected))
                    || (value.PriceType == PriceType.Unannounced && value.SourceFactId != null)))
            .ToArrayAsync(cancellationToken);
        var specRows = await (
                from spec in database.TrimSpecs.AsNoTracking()
                join definition in database.SpecDefinitions.AsNoTracking() on spec.SpecDefinitionId equals definition.Id
                where activeIds.Contains(spec.TrimId) && CoreSpecCodes.Contains(definition.Code)
                select new { spec.TrimId, definition.Code, spec.SourceFactId })
            .ToArrayAsync(cancellationToken);
        var profileTrimIds = await database.PowertrainProfiles.AsNoTracking()
            .Where(value => activeIds.Contains(value.TrimId))
            .Select(value => value.TrimId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var sourceRows = await database.Sources.AsNoTracking().Where(value => value.Active).ToArrayAsync(cancellationToken);
        var latestSnapshots = await database.SourceSnapshots.AsNoTracking().GroupBy(value => value.SourceId)
            .Select(group => new { SourceId = group.Key, LastFetchedAt = group.Max(value => value.FetchedAt) })
            .ToArrayAsync(cancellationToken);
        var staleSourceIds = sourceRows.Where(value =>
        {
            var last = value.LastFetchedAt ?? latestSnapshots.FirstOrDefault(snapshot => snapshot.SourceId == value.Id)?.LastFetchedAt;
            return last is null || last + value.RefreshInterval < now;
        }).Select(value => value.Id).ToArray();
        var staleTrimIds = await (
                from price in database.Prices.AsNoTracking()
                join fact in database.SourceFacts.AsNoTracking() on price.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                where activeIds.Contains(price.TrimId) && staleSourceIds.Contains(snapshot.SourceId)
                select price.TrimId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var blockedTrimIds = await database.DataChanges.AsNoTracking()
            .Where(value => value.EntityType == "Trim"
                && activeIds.Contains(value.EntityId)
                && value.Status == ChangeStatus.PendingReview
                && (value.RiskLevel == ChangeRiskLevel.High || value.RiskLevel == ChangeRiskLevel.Critical))
            .Select(value => value.EntityId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        const int requiredPerTrim = 9; // market status + price/provenance + powertrain + five canonical specs
        var coverageBrands = new List<AdminCoverageBrand>();
        foreach (var brand in brands)
        {
            var latestScope = scopes.FirstOrDefault(value => value.BrandId == brand.Id);
            var included = latestScope?.Included == true;
            var brandRows = activeRows.Where(value => value.BrandId == brand.Id).ToArray();
            var trimIds = brandRows.Select(value => value.Trim.Id).ToArray();
            var completeFields = 0;
            var missing = 0;
            foreach (var trimId in trimIds)
            {
                var hasPrice = currentPrices.Any(value => value.TrimId == trimId);
                var hasPriceSource = currentPrices.Any(value => value.TrimId == trimId && value.SourceFactId is not null);
                var hasPowertrain = profileTrimIds.Contains(trimId);
                var coreSpecCount = specRows.Where(value => value.TrimId == trimId).Select(value => value.Code).Distinct().Count();
                var present = 1 + (hasPrice ? 1 : 0) + (hasPriceSource ? 1 : 0) + (hasPowertrain ? 1 : 0) + coreSpecCount;
                completeFields += present;
                missing += requiredPerTrim - present;
            }
            var completeness = trimIds.Length == 0 ? 0 : decimal.Round((decimal)completeFields / (trimIds.Length * requiredPerTrim), 6);
            var staleCount = trimIds.Count(id => staleTrimIds.Contains(id));
            var freshness = trimIds.Length == 0 ? 0 : decimal.Round((decimal)(trimIds.Length - staleCount) / trimIds.Length, 6);
            var published = trimIds.Count(id => currentPrices.Any(value => value.TrimId == id));
            var blocked = trimIds.Count(id => blockedTrimIds.Contains(id)) + trimIds.Length - published;
            coverageBrands.Add(new AdminCoverageBrand(
                brand.Id,
                brand.Name,
                included,
                trimIds.Length,
                trimIds.Length,
                published,
                blocked,
                staleCount,
                completeness,
                freshness,
                missing));
        }

        var includedRows = coverageBrands.Where(value => value.Included).ToArray();
        var totalActive = includedRows.Sum(value => value.Mapped);
        var completenessTotal = totalActive == 0
            ? 0
            : decimal.Round(includedRows.Sum(value => value.Completeness * value.Mapped) / totalActive, 6);
        var freshnessTotal = totalActive == 0
            ? 0
            : decimal.Round(includedRows.Sum(value => value.Freshness * value.Mapped) / totalActive, 6);
        var duplicates = DuplicateTrimGroups(trimRows).Count;
        var failures = new List<string>();
        if (includedRows.Length < 15) failures.Add("BRAND_SCOPE_BELOW_INITIAL_VALIDATION_TARGET");
        if (includedRows.Any(value => value.Discovered == 0)) failures.Add("INCLUDED_BRAND_WITHOUT_ACTIVE_MODEL_OR_TRIM");
        if (includedRows.Any(value => value.Published < value.Mapped)) failures.Add("ACTIVE_TRIM_WITHOUT_VALID_PRICE_STATE");
        if (completenessTotal < 0.95m) failures.Add("CORE_FIELD_COVERAGE_BELOW_95_PERCENT");
        if (freshnessTotal < 1m) failures.Add("PRICE_OR_SOURCE_FRESHNESS_SLA_FAILED");
        if (duplicates > 0) failures.Add("UNRESOLVED_HIGH_CONFIDENCE_DUPLICATE");
        if (!await CurrentRulesAndTariffsAreVerifiedAsync(now, cancellationToken)) failures.Add("LEGAL_OR_ENERGY_SOURCE_NOT_CURRENT");
        return new AdminCoverageResponse(
            coverageBrands,
            includedRows.Length,
            activeRows.Select(value => value.ModelId).Distinct().Count(),
            totalActive,
            completenessTotal,
            freshnessTotal,
            duplicates,
            failures.Count == 0,
            failures,
            now);
    }

    public async Task<AdminQualityResponse> GetQualityAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var issues = new List<AdminQualityIssue>();
        var specRows = await (
                from value in database.TrimSpecs.AsNoTracking()
                join definition in database.SpecDefinitions.AsNoTracking() on value.SpecDefinitionId equals definition.Id
                where value.NumericValue != null
                select new { Value = value, Definition = definition })
            .ToArrayAsync(cancellationToken);
        foreach (var row in specRows.Where(row =>
                     (row.Definition.MinimumNumericValue is not null && row.Value.NumericValue < row.Definition.MinimumNumericValue)
                     || (row.Definition.MaximumNumericValue is not null && row.Value.NumericValue > row.Definition.MaximumNumericValue)
                     || IsImpossibleDimension(row.Definition.Code, row.Value.NumericValue!.Value)))
        {
            issues.Add(Issue("IMPOSSIBLE_SPEC_VALUE", "High", "TrimSpec", row.Value.Id, row.Definition.Code, $"Numeric value {row.Value.NumericValue} is outside the canonical sanity range."));
        }
        var trimRows = await (
                from trim in database.Trims.AsNoTracking()
                join modelYear in database.ModelYears.AsNoTracking() on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations.AsNoTracking() on modelYear.GenerationId equals generation.Id
                join model in database.Models.AsNoTracking() on generation.ModelId equals model.Id
                select new QualityTrimRow(trim, model.Id, model.BrandId, generation.Code, modelYear.Year))
            .ToArrayAsync(cancellationToken);
        foreach (var group in DuplicateTrimGroups(trimRows))
        {
            foreach (var row in group)
            {
                issues.Add(Issue("DUPLICATE_TRIM", "High", "Trim", row.Trim.Id, "normalizedKey", "Two trim identities normalize to the same brand/model/generation/model-year/name key."));
            }
        }
        var activeSources = await database.Sources.AsNoTracking().Where(value => value.Active).ToArrayAsync(cancellationToken);
        var sourceSnapshots = await database.SourceSnapshots.AsNoTracking().GroupBy(value => value.SourceId)
            .Select(group => new { SourceId = group.Key, LastFetchedAt = group.Max(value => value.FetchedAt) })
            .ToArrayAsync(cancellationToken);
        var staleSources = activeSources.Select(value => new
        {
            Source = value,
            LastFetchedAt = value.LastFetchedAt ?? sourceSnapshots.FirstOrDefault(snapshot => snapshot.SourceId == value.Id)?.LastFetchedAt,
        }).Where(value => value.LastFetchedAt is null || value.LastFetchedAt + value.Source.RefreshInterval < now).ToArray();
        issues.AddRange(staleSources.Select(value => Issue(
            "SOURCE_STALE",
            value.Source.AuthorityLevel is SourceAuthorityLevel.CompetentAuthority or SourceAuthorityLevel.BrandOfficial ? "High" : "Medium",
            "Source",
            value.Source.Id,
            "lastFetchedAt",
            value.LastFetchedAt is null ? "Active source has never been fetched." : $"Source exceeded its {value.Source.RefreshInterval.TotalHours:0}-hour refresh interval.")));

        var activeTrimIds = trimRows.Where(value => value.Trim.MarketStatus is MarketStatus.Active or MarketStatus.Upcoming or MarketStatus.Announced).Select(value => value.Trim.Id).ToArray();
        var prices = await database.Prices.AsNoTracking()
            .Where(value => activeTrimIds.Contains(value.TrimId)
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || value.EffectiveTo > now)
                && value.RegionScope == "VN"
                && ((value.PriceType == PriceType.Msrp && (value.Status == PriceStatus.Official || value.Status == PriceStatus.Expected))
                    || (value.PriceType == PriceType.Unannounced && value.SourceFactId != null)))
            .ToArrayAsync(cancellationToken);
        var specs = await (
                from value in database.TrimSpecs.AsNoTracking()
                join definition in database.SpecDefinitions.AsNoTracking() on value.SpecDefinitionId equals definition.Id
                where activeTrimIds.Contains(value.TrimId) && CoreSpecCodes.Contains(definition.Code)
                select new { value.TrimId, definition.Code })
            .ToArrayAsync(cancellationToken);
        var powertrains = await database.PowertrainProfiles.AsNoTracking().Where(value => activeTrimIds.Contains(value.TrimId)).Select(value => value.TrimId).ToArrayAsync(cancellationToken);
        foreach (var trimId in activeTrimIds)
        {
            if (!prices.Any(value => value.TrimId == trimId)) issues.Add(Issue("CORE_PRICE_MISSING", "Critical", "Trim", trimId, "price", "ACTIVE/COMING_SOON trim has no current MSRP, expected or explicit unannounced price state."));
            if (!prices.Any(value => value.TrimId == trimId && value.SourceFactId is not null)) issues.Add(Issue("CORE_PRICE_SOURCE_MISSING", "Critical", "Trim", trimId, "price.sourceFactId", "Current core price state has no source fact."));
            if (!powertrains.Contains(trimId)) issues.Add(Issue("CORE_POWERTRAIN_MISSING", "High", "Trim", trimId, "powertrain", "Trim has no sourced powertrain profile."));
            foreach (var code in CoreSpecCodes.Where(code => !specs.Any(value => value.TrimId == trimId && value.Code == code)))
            {
                issues.Add(Issue("CORE_SPEC_MISSING", "High", "Trim", trimId, code, "Canonical core fact is absent; publish an explicit UNKNOWN fact if the source does not disclose it."));
            }
        }

        var factRows = await database.SourceFacts.AsNoTracking()
            .Where(value => value.EntityId != null && value.NormalizedValue != null && (value.Status == FactStatus.Official || value.Status == FactStatus.Expected))
            .Select(value => new { value.Id, value.EntityType, value.EntityId, value.FieldPath, value.NormalizedValue })
            .ToArrayAsync(cancellationToken);
        foreach (var conflict in factRows.GroupBy(value => new { value.EntityType, value.EntityId, value.FieldPath })
                     .Where(group => group.Select(value => value.NormalizedValue).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            issues.Add(Issue("SOURCE_FACT_CONFLICT", "High", conflict.Key.EntityType, conflict.Key.EntityId!.Value, conflict.Key.FieldPath, "Official/expected source facts disagree on normalized value and require review."));
        }

        var offerRows = await (
                from offer in database.DealerOffers.AsNoTracking()
                join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
                select new { Offer = offer, Branch = branch })
            .ToArrayAsync(cancellationToken);
        var offerIds = offerRows.Select(value => value.Offer.Id).ToArray();
        var benefits = await database.DealerOfferBenefits.AsNoTracking().Where(value => offerIds.Contains(value.OfferId)).ToArrayAsync(cancellationToken);
        foreach (var row in offerRows)
        {
            var input = new DealerOfferQualityInput(
                row.Offer.Id,
                row.Branch.Id,
                row.Branch.ProvinceCode,
                row.Offer.TrimId,
                row.Offer.EffectiveFrom,
                row.Offer.EffectiveTo,
                row.Offer.Status.ToString(),
                row.Offer.ConditionsJson,
                row.Offer.SourceFactId is not null || !string.IsNullOrWhiteSpace(row.Offer.ManualOverrideReason),
                benefits.Where(value => value.OfferId == row.Offer.Id).Select(value => new DealerOfferBenefitQualityInput(
                    value.Id, value.Type.ToString(), value.ExclusivityGroup, value.CashValue, value.StatedValue)).ToArray());
            issues.AddRange(DealerOfferQualityEvaluator.Evaluate(input, now).Select(value => Issue(value.Code, value.Severity, value.EntityType, value.EntityId, value.FieldPath, value.Message)));
        }
        var unsafeImages = await database.VehicleImages.AsNoTracking()
            .Where(value => value.StorageUrl != null && value.RightsStatus == RightsStatus.Unknown)
            .ToArrayAsync(cancellationToken);
        issues.AddRange(unsafeImages.Select(value => Issue("IMAGE_RIGHTS_UNKNOWN", "Critical", "VehicleImage", value.Id, "rightsStatus", "Stored/public image URL has UNKNOWN rights and must not be published.")));

        issues = issues.OrderBy(value => SeverityRank(value.Severity)).ThenBy(value => value.Code).Take(1000).ToList();
        return new AdminQualityResponse(
            issues,
            issues.Count(value => value.Code.Contains("IMPOSSIBLE", StringComparison.Ordinal)),
            issues.Count(value => value.Code.Contains("DUPLICATE", StringComparison.Ordinal)),
            issues.Count(value => value.Code == "SOURCE_STALE"),
            issues.Count(value => value.Code.StartsWith("CORE_", StringComparison.Ordinal)),
            issues.Count(value => value.Code == "SOURCE_FACT_CONFLICT"),
            issues.Count(value => value.Code.StartsWith("DEALER_OFFER_", StringComparison.Ordinal)),
            now);
    }

    public async Task<IReadOnlyList<AdminAuditResponse>> GetAuditAsync(int take, CancellationToken cancellationToken) =>
        await database.AuditEvents.AsNoTracking()
            .OrderByDescending(value => value.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(value => new AdminAuditResponse(
                value.Id, value.Actor, value.Action, value.EntityType, value.EntityId, value.BeforeJson,
                value.AfterJson, value.Reason, value.OccurredAt, value.CorrelationId))
            .ToArrayAsync(cancellationToken);

    private async Task<bool> CurrentRulesAndTariffsAreVerifiedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ruleSourcesCurrent = await (
                from rule in database.RegistrationRules.AsNoTracking()
                join fact in database.SourceFacts.AsNoTracking() on rule.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where rule.EffectiveFrom <= now && (rule.EffectiveTo == null || rule.EffectiveTo > now)
                select source.Active && snapshot.FetchedAt + source.RefreshInterval >= now)
            .AllAsync(value => value, cancellationToken);
        var energySourcesCurrent = await (
                from price in database.EnergyPrices.AsNoTracking()
                join fact in database.SourceFacts.AsNoTracking() on price.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where price.EffectiveFrom <= now && (price.EffectiveTo == null || price.EffectiveTo > now)
                select source.Active && snapshot.FetchedAt + source.RefreshInterval >= now)
            .AllAsync(value => value, cancellationToken);
        return ruleSourcesCurrent && energySourcesCurrent;
    }

    private static List<IGrouping<string, QualityTrimRow>> DuplicateTrimGroups(IEnumerable<QualityTrimRow> rows) =>
        rows.GroupBy(
                row => $"{row.BrandId}|{row.ModelId}|{row.GenerationCode}|{row.Year}|{Normalize(row.Trim.Name)}",
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

    private static string Normalize(string value) => string.Concat(value.ToLowerInvariant().Where(char.IsLetterOrDigit));

    private static bool IsImpossibleDimension(string code, decimal value) => code switch
    {
        "LENGTH_MM" => value is < 2500 or > 7000,
        "WIDTH_MM" => value is < 1200 or > 3000,
        "HEIGHT_MM" => value is < 1000 or > 3500,
        "WHEELBASE_MM" => value is < 1500 or > 5000,
        "SEATS" => value is < 1 or > 80,
        _ => false,
    };

    private static AdminQualityIssue Issue(string code, string severity, string entityType, Guid entityId, string fieldPath, string message) =>
        new(code, severity, entityType, entityId, fieldPath, message);

    private static int SeverityRank(string value) => value switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        _ => 3,
    };

    private sealed record QualityTrimRow(Trim Trim, Guid ModelId, Guid BrandId, string GenerationCode, int Year);
}
