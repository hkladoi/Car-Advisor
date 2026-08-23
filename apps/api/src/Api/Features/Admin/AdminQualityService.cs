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
        const string market = "VN";
        var brands = await database.Brands.AsNoTracking().OrderBy(value => value.Name).ToArrayAsync(cancellationToken);
        var currentScopeRows = await database.BrandScopes.AsNoTracking()
            .Where(value => value.Market == market && value.EffectiveFrom <= now && (value.EffectiveTo == null || value.EffectiveTo > now))
            .OrderByDescending(value => value.EffectiveFrom)
            .ToArrayAsync(cancellationToken);
        var scopes = currentScopeRows.GroupBy(value => value.BrandId).Select(group => group.First()).ToArray();
        var latestReview = await database.MarketScopeReviews.AsNoTracking()
            .Where(value => value.Market == market)
            .OrderByDescending(value => value.ReviewedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var candidates = await database.MarketCandidates.AsNoTracking()
            .Where(value => value.Market == market && (value.MarketStatus == MarketStatus.Active || value.MarketStatus == MarketStatus.Upcoming || value.MarketStatus == MarketStatus.Announced))
            .ToArrayAsync(cancellationToken);
        var trimRows = await (
                from trim in database.Trims.AsNoTracking()
                join modelYear in database.ModelYears.AsNoTracking() on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations.AsNoTracking() on modelYear.GenerationId equals generation.Id
                join model in database.Models.AsNoTracking() on generation.ModelId equals model.Id
                select new QualityTrimRow(trim, model.Id, model.BrandId, generation.Code, modelYear.Year))
            .ToArrayAsync(cancellationToken);
        var includedBrandIds = scopes.Where(value => value.Included).Select(value => value.BrandId).ToHashSet();
        var activeRows = trimRows.Where(value => includedBrandIds.Contains(value.BrandId) && value.Trim.MarketStatus is MarketStatus.Active or MarketStatus.Upcoming or MarketStatus.Announced).ToArray();
        var publishedTrimCandidates = candidates
            .Where(value => value.Kind == MarketCandidateKind.Trim && value.Resolution == MarketCandidateResolution.Published && value.TrimId is not null)
            .ToArray();
        var activeIds = publishedTrimCandidates.Select(value => value.TrimId!.Value).Distinct().ToArray();
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
        var profiles = await database.PowertrainProfiles.AsNoTracking()
            .Where(value => activeIds.Contains(value.TrimId))
            .ToArrayAsync(cancellationToken);
        var sourceRows = await database.Sources.AsNoTracking().Where(value => value.Active).ToArrayAsync(cancellationToken);
        var latestSnapshots = await database.SourceSnapshots.AsNoTracking().GroupBy(value => value.SourceId)
            .Select(group => new { SourceId = group.Key, LastFetchedAt = group.Max(value => value.FetchedAt) })
            .ToArrayAsync(cancellationToken);
        var evidenceSnapshotIds = candidates.Select(value => value.EvidenceSnapshotId)
            .Concat(scopes.Where(value => value.EvidenceSnapshotId is not null).Select(value => value.EvidenceSnapshotId!.Value))
            .Distinct()
            .ToArray();
        var evidenceSnapshots = await database.SourceSnapshots.AsNoTracking()
            .Where(value => evidenceSnapshotIds.Contains(value.Id))
            .ToArrayAsync(cancellationToken);
        var staleSourceIds = sourceRows.Where(value =>
        {
            var last = value.LastFetchedAt ?? latestSnapshots.FirstOrDefault(snapshot => snapshot.SourceId == value.Id)?.LastFetchedAt;
            return last is null || last + value.RefreshInterval < now;
        }).Select(value => value.Id).ToHashSet();
        var staleCandidateIds = candidates.Where(candidate =>
        {
            var source = sourceRows.FirstOrDefault(value => value.Id == candidate.SourceId);
            var snapshot = evidenceSnapshots.FirstOrDefault(value => value.Id == candidate.EvidenceSnapshotId);
            return source is null || snapshot is null || snapshot.SourceId != candidate.SourceId || snapshot.HttpStatus is < 200 or >= 300
                || staleSourceIds.Contains(candidate.SourceId) || candidate.LastSeenAt + source.RefreshInterval < now;
        }).Select(value => value.Id).ToHashSet();

        var priceSourceIds = await (
                from price in database.Prices.AsNoTracking()
                join fact in database.SourceFacts.AsNoTracking() on price.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                where activeIds.Contains(price.TrimId) && price.EffectiveFrom <= now && (price.EffectiveTo == null || price.EffectiveTo > now)
                select snapshot.SourceId)
            .Distinct().ToArrayAsync(cancellationToken);
        var energySourceIds = await (
                from price in database.EnergyPrices.AsNoTracking()
                join fact in database.SourceFacts.AsNoTracking() on price.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                where price.EffectiveFrom <= now && (price.EffectiveTo == null || price.EffectiveTo > now)
                select snapshot.SourceId)
            .Distinct().ToArrayAsync(cancellationToken);
        var legalSourceIds = await (
                from rule in database.RegistrationRules.AsNoTracking()
                join fact in database.SourceFacts.AsNoTracking() on rule.SourceFactId equals fact.Id
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                where rule.EffectiveFrom <= now && (rule.EffectiveTo == null || rule.EffectiveTo > now)
                select snapshot.SourceId)
            .Distinct().ToArrayAsync(cancellationToken);
        var promotionSourceIds = sourceRows.Where(value => value.Category is "dealer-offer" or "finance-campaign").Select(value => value.Id).ToArray();
        var dealerOfferSourceIds = sourceRows.Where(value => value.Category == "dealer-offer").Select(value => value.Id).ToArray();

        AdminFreshnessDomain DomainFreshness(string name, IReadOnlyCollection<Guid> sourceIds)
        {
            var distinct = sourceIds.Distinct().ToArray();
            var stale = distinct.Count(id => staleSourceIds.Contains(id));
            var freshness = distinct.Length == 0 ? 0m : decimal.Round((decimal)(distinct.Length - stale) / distinct.Length, 6);
            return new AdminFreshnessDomain(name, distinct.Length, stale, freshness, distinct.Length > 0 && stale == 0);
        }

        var freshnessDomains = new[]
        {
            DomainFreshness("price", priceSourceIds),
            DomainFreshness("promotion", promotionSourceIds),
            DomainFreshness("dealer-offer", dealerOfferSourceIds),
            DomainFreshness("energy", energySourceIds),
            DomainFreshness("legal", legalSourceIds),
        };

        const int requiredPerTrim = 9; // market status + price/provenance + powertrain + five canonical specs
        var coverageBrands = new List<AdminCoverageBrand>();
        foreach (var scope in scopes.OrderBy(value => brands.FirstOrDefault(brand => brand.Id == value.BrandId)?.Name))
        {
            var brand = brands.First(value => value.Id == scope.BrandId);
            var brandCandidates = candidates.Where(value => value.BrandId == brand.Id).ToArray();
            var trimCandidates = brandCandidates.Where(value => value.Kind == MarketCandidateKind.Trim).ToArray();
            var modelCandidates = brandCandidates.Where(value => value.Kind == MarketCandidateKind.Model).ToArray();
            var trimIds = trimCandidates.Where(value => value.Resolution == MarketCandidateResolution.Published && value.TrimId != null).Select(value => value.TrimId!.Value).Distinct().ToArray();
            var completeFields = 0;
            var missing = 0;
            foreach (var trimId in trimIds)
            {
                var hasPrice = currentPrices.Any(value => value.TrimId == trimId);
                var hasPriceSource = currentPrices.Any(value => value.TrimId == trimId && value.SourceFactId is not null);
                var hasPowertrain = profiles.Any(value => value.TrimId == trimId && value.SourceFactId is not null);
                var coreSpecCount = specRows.Where(value => value.TrimId == trimId && value.SourceFactId != null).Select(value => value.Code).Distinct().Count();
                var present = 1 + (hasPrice ? 1 : 0) + (hasPriceSource ? 1 : 0) + (hasPowertrain ? 1 : 0) + coreSpecCount;
                completeFields += present;
                missing += requiredPerTrim - present;
            }
            var completeness = trimIds.Length == 0 ? 1m : decimal.Round((decimal)completeFields / (trimIds.Length * requiredPerTrim), 6);
            var staleCount = brandCandidates.Count(value => staleCandidateIds.Contains(value.Id));
            var freshness = brandCandidates.Length == 0 ? (scope.Included ? 0m : 1m) : decimal.Round((decimal)(brandCandidates.Length - staleCount) / brandCandidates.Length, 6);
            var published = brandCandidates.Count(value => value.Resolution == MarketCandidateResolution.Published);
            var inventoryGaps = modelCandidates.Count(value => value.TrimInventoryStatus == TrimInventoryStatus.BlockedWithReason);
            var blocked = brandCandidates.Count(value => value.Resolution == MarketCandidateResolution.BlockedWithReason) + inventoryGaps;
            var scopeSnapshot = scope.EvidenceSnapshotId is null ? null : evidenceSnapshots.FirstOrDefault(value => value.Id == scope.EvidenceSnapshotId);
            var reviewed = scope.ReviewedAt is not null && !string.IsNullOrWhiteSpace(scope.ReviewedBy)
                && scope.SourceId is not null && scopeSnapshot is not null && scopeSnapshot.SourceId == scope.SourceId
                && scopeSnapshot.HttpStatus is >= 200 and < 300;
            coverageBrands.Add(new AdminCoverageBrand(
                brand.Id,
                brand.Name,
                scope.Included,
                brandCandidates.Length,
                brandCandidates.Count(value => value.Resolution == MarketCandidateResolution.Published),
                published,
                blocked,
                staleCount,
                completeness,
                freshness,
                missing,
                modelCandidates.Length,
                trimCandidates.Length,
                inventoryGaps,
                reviewed,
                scope.ReviewedAt));
        }

        var includedRows = coverageBrands.Where(value => value.Included).ToArray();
        var totalActive = activeIds.Length;
        var missingCoreTotal = includedRows.Sum(value => value.MissingCoreCount);
        var completenessTotal = totalActive == 0
            ? 0
            : decimal.Round((decimal)(totalActive * requiredPerTrim - missingCoreTotal) / (totalActive * requiredPerTrim), 6);
        var includedCandidateCount = candidates.Count(value => includedBrandIds.Contains(value.BrandId));
        var freshnessTotal = includedCandidateCount == 0 ? 0 : decimal.Round((decimal)(includedCandidateCount - candidates.Count(value => includedBrandIds.Contains(value.BrandId) && staleCandidateIds.Contains(value.Id))) / includedCandidateCount, 6);
        var duplicates = DuplicateTrimGroups(trimRows).Count;
        var failures = new List<string>();
        if (latestReview is null) failures.Add("MARKET_SCOPE_REVIEW_MISSING");
        if (latestReview is not null && latestReview.ReviewedBrandCount != scopes.Length) failures.Add("MARKET_SCOPE_REVIEW_COUNT_MISMATCH");
        if (latestReview is not null && latestReview.IncludedBrandCount != includedRows.Length) failures.Add("MARKET_SCOPE_INCLUDED_COUNT_MISMATCH");
        if (latestReview is not null && latestReview.ExcludedBrandCount != coverageBrands.Count(value => !value.Included)) failures.Add("MARKET_SCOPE_EXCLUDED_COUNT_MISMATCH");
        if (latestReview is not null && latestReview.ModelCandidateCount != candidates.Count(value => includedBrandIds.Contains(value.BrandId) && value.Kind == MarketCandidateKind.Model)) failures.Add("MODEL_CANDIDATE_COUNT_MISMATCH");
        if (latestReview is not null && latestReview.TrimCandidateCount != candidates.Count(value => includedBrandIds.Contains(value.BrandId) && value.Kind == MarketCandidateKind.Trim)) failures.Add("TRIM_CANDIDATE_COUNT_MISMATCH");
        if (coverageBrands.Any(value => !value.Reviewed)) failures.Add("BRAND_SCOPE_NOT_FULLY_REVIEWED");
        if (!includedRows.Any(value => value.BrandName == "Porsche")) failures.Add("PORSCHE_REQUIRED_IN_BRAND_SCOPE");
        if (coverageBrands.Any(value => value.Included && value.BrandName is "Ferrari" or "Lamborghini" or "Lotus")) failures.Add("CONFIGURED_SUPERCAR_EXCLUSION_VIOLATED");
        if (includedRows.Any(value => value.ModelCandidates == 0)) failures.Add("INCLUDED_BRAND_WITHOUT_ACTIVE_MODEL_CANDIDATE");
        if (candidates.Any(value => includedBrandIds.Contains(value.BrandId) && staleCandidateIds.Contains(value.Id))) failures.Add("MARKET_CANDIDATE_SOURCE_FRESHNESS_SLA_FAILED");
        if (candidates.Any(value => includedBrandIds.Contains(value.BrandId) && value.Resolution == MarketCandidateResolution.Published
                && (value.ModelId is null || (value.Kind == MarketCandidateKind.Trim && value.TrimId is null)))) failures.Add("PUBLISHED_CANDIDATE_WITHOUT_CATALOG_MAPPING");
        if (candidates.Any(value => includedBrandIds.Contains(value.BrandId) && value.Resolution == MarketCandidateResolution.BlockedWithReason
                && string.IsNullOrWhiteSpace(value.BlockedReason))) failures.Add("BLOCKED_CANDIDATE_WITHOUT_REASON");
        if (candidates.Any(value => includedBrandIds.Contains(value.BrandId) && value.Kind == MarketCandidateKind.Model
                && value.TrimInventoryStatus == TrimInventoryStatus.BlockedWithReason && string.IsNullOrWhiteSpace(value.TrimInventoryReason))) failures.Add("UNEXPLAINED_TRIM_INVENTORY_GAP");
        var mappedTrimIds = publishedTrimCandidates.Select(value => value.TrimId!.Value).ToHashSet();
        if (activeRows.Any(value => !mappedTrimIds.Contains(value.Trim.Id))) failures.Add("ACTIVE_CATALOG_TRIM_NOT_IN_MARKET_INVENTORY");
        if (activeIds.Any(trimId => !currentPrices.Any(value => value.TrimId == trimId && value.SourceFactId is not null))) failures.Add("ACTIVE_TRIM_WITHOUT_VALID_PRICE_STATE");
        if (completenessTotal < 0.95m) failures.Add("CORE_FIELD_COVERAGE_BELOW_95_PERCENT");
        foreach (var domain in freshnessDomains.Where(value => !value.Passed)) failures.Add($"{domain.Domain.ToUpperInvariant().Replace('-', '_')}_FRESHNESS_SLA_FAILED");
        if (duplicates > 0) failures.Add("UNRESOLVED_HIGH_CONFIDENCE_DUPLICATE");
        var gaps = candidates.Where(value => includedBrandIds.Contains(value.BrandId))
            .SelectMany(value =>
            {
                var brandName = brands.First(brand => brand.Id == value.BrandId).Name;
                var result = new List<AdminCoverageGap>();
                if (value.Resolution == MarketCandidateResolution.BlockedWithReason)
                    result.Add(new AdminCoverageGap(value.Id, brandName, value.Kind.ToString(), value.Name, "BLOCKED_WITH_REASON", value.BlockedReason!, value.LastSeenAt));
                if (value.Kind == MarketCandidateKind.Model && value.TrimInventoryStatus == TrimInventoryStatus.BlockedWithReason)
                    result.Add(new AdminCoverageGap(value.Id, brandName, value.Kind.ToString(), value.Name, "TRIM_INVENTORY_BLOCKED_WITH_REASON", value.TrimInventoryReason!, value.LastSeenAt));
                return result;
            }).OrderBy(value => value.BrandName).ThenBy(value => value.CandidateName).ToArray();
        return new AdminCoverageResponse(
            coverageBrands,
            coverageBrands.Count,
            candidates.Count(value => includedBrandIds.Contains(value.BrandId) && value.Kind == MarketCandidateKind.Model),
            candidates.Count(value => includedBrandIds.Contains(value.BrandId) && value.Kind == MarketCandidateKind.Trim),
            completenessTotal,
            freshnessTotal,
            duplicates,
            failures.Count == 0,
            failures,
            latestReview?.SchemaVersion,
            latestReview?.ManifestHash,
            coverageBrands.Count(value => value.Reviewed),
            coverageBrands.Count(value => !value.Included),
            includedCandidateCount,
            candidates.Count(value => includedBrandIds.Contains(value.BrandId) && value.Resolution is MarketCandidateResolution.Published or MarketCandidateResolution.BlockedWithReason),
            candidates.Count(value => includedBrandIds.Contains(value.BrandId) && value.Resolution == MarketCandidateResolution.BlockedWithReason)
                + includedRows.Sum(value => value.TrimInventoryGaps),
            includedRows.Sum(value => value.TrimInventoryGaps),
            gaps,
            freshnessDomains,
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
