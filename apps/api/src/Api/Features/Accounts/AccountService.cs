using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Accounts;
using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Accounts;

public interface IAccountService
{
    Task<AccountSessionResponse> GetSessionAsync(AccountActor actor, CancellationToken cancellationToken);
    Task<AccountProfileResponse?> GetProfileAsync(AccountActor actor, CancellationToken cancellationToken);
    Task<AccountProfileResponse> SaveProfileAsync(AccountActor actor, AccountProfileRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedComparisonResponse>> GetComparisonsAsync(AccountActor actor, CancellationToken cancellationToken);
    Task<SavedComparisonResponse> SaveComparisonAsync(AccountActor actor, SavedComparisonRequest request, CancellationToken cancellationToken);
    Task DeleteComparisonAsync(AccountActor actor, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<WatchlistResponse>> GetWatchlistAsync(AccountActor actor, CancellationToken cancellationToken);
    Task<WatchlistResponse> SaveWatchlistAsync(AccountActor actor, WatchlistRequest request, CancellationToken cancellationToken);
    Task DeleteWatchlistAsync(AccountActor actor, Guid trimId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountAlertResponse>> GetAlertsAsync(AccountActor actor, CancellationToken cancellationToken);
    Task<AccountDataExportResponse> ExportAsync(AccountActor actor, CancellationToken cancellationToken);
}

public sealed class AccountService(AppDbContext database, TimeProvider timeProvider) : IAccountService
{
    private static readonly string[] ProfilePresets = ["lean-city", "city-balanced", "high-mileage-public"];
    private static readonly string[] FinancingPresets = ["cash-preset", "standard-loan", "short-reducing"];

    public async Task<AccountSessionResponse> GetSessionAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var account = await database.UserAccounts.AsNoTracking().SingleAsync(value => value.Id == actor.UserId, cancellationToken);
        return new AccountSessionResponse(account.Id, account.Email, account.DisplayName, actor.ExpiresAt,
            account.ConsentedAt, account.PrivacyPolicyVersion);
    }

    public async Task<AccountProfileResponse?> GetProfileAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var owner = actor.UserId.ToString("D");
        var profile = await database.AffordabilityProfiles.AsNoTracking()
            .Where(value => value.OwnerSubjectId == owner)
            .OrderByDescending(value => value.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return profile is null ? null : MapProfile(profile);
    }

    public async Task<AccountProfileResponse> SaveProfileAsync(
        AccountActor actor,
        AccountProfileRequest request,
        CancellationToken cancellationToken)
    {
        var name = ValidateText(request.Name, 2, 80, "ACCOUNT_PROFILE_NAME_INVALID", "Profile name must contain 2 to 80 characters.");
        var region = await ValidateRegionAsync(request.RegionCode, cancellationToken);
        if (!Enum.TryParse<AffordabilityPolicy>(request.Policy, true, out var policy))
        {
            throw Error(400, "ACCOUNT_PROFILE_POLICY_INVALID", "Policy must be Conservative, Balanced, Aggressive or Custom.");
        }
        var values = new[]
        {
            request.NetMonthlyIncome, request.RentHousing, request.EssentialExpenses, request.OtherFixedDebt,
            request.SavingsTarget, request.MonthlyKilometres, request.ParkingMonthly, request.HouseholdBaseKwh,
        };
        if (values.Any(value => value < 0))
        {
            throw Error(400, "ACCOUNT_PROFILE_VALUE_NEGATIVE", "Profile amounts, distance and household energy must be non-negative.");
        }

        var owner = actor.UserId.ToString("D");
        var now = timeProvider.GetUtcNow();
        var profile = await database.AffordabilityProfiles
            .Where(value => value.OwnerSubjectId == owner)
            .OrderByDescending(value => value.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            profile = new AffordabilityProfile
            {
                OwnerSubjectId = owner,
                CreatedAt = now,
            };
            database.AffordabilityProfiles.Add(profile);
        }
        profile.Name = name;
        profile.RegionCode = region;
        profile.NetMonthlyIncome = request.NetMonthlyIncome;
        profile.RentHousing = request.RentHousing;
        profile.EssentialExpenses = request.EssentialExpenses;
        profile.OtherFixedDebt = request.OtherFixedDebt;
        profile.SavingsTarget = request.SavingsTarget;
        profile.MonthlyKilometres = request.MonthlyKilometres;
        profile.ParkingMonthly = request.ParkingMonthly;
        profile.HouseholdBaseKwh = request.HouseholdBaseKwh;
        profile.Policy = policy;
        profile.AssumptionsJson = JsonSerializer.Serialize(new
        {
            persistedByExplicitConsent = true,
            privacyPolicyVersion = AccountAuthService.PrivacyPolicyVersion,
        });
        profile.UpdatedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        return MapProfile(profile);
    }

    public async Task<IReadOnlyList<SavedComparisonResponse>> GetComparisonsAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var rows = await database.SavedComparisons.AsNoTracking()
            .Where(value => value.UserAccountId == actor.UserId)
            .OrderByDescending(value => value.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        return rows.Select(MapComparison).ToArray();
    }

    public async Task<SavedComparisonResponse> SaveComparisonAsync(
        AccountActor actor,
        SavedComparisonRequest request,
        CancellationToken cancellationToken)
    {
        var name = ValidateText(request.Name, 2, 160, "ACCOUNT_COMPARISON_NAME_INVALID", "Comparison name must contain 2 to 160 characters.");
        var trimIds = request.TrimIds.Distinct().ToArray();
        if (trimIds.Length is < 2 or > 4)
        {
            throw Error(400, "ACCOUNT_COMPARISON_TRIMS_INVALID", "A saved comparison must contain 2 to 4 distinct trims.");
        }
        var found = await database.Trims.AsNoTracking().CountAsync(value => trimIds.Contains(value.Id), cancellationToken);
        if (found != trimIds.Length)
        {
            throw Error(404, "ACCOUNT_COMPARISON_TRIM_NOT_FOUND", "At least one selected trim was not found.");
        }
        var region = await ValidateRegionAsync(request.RegionCode, cancellationToken);
        var profilePreset = ValidateChoice(request.ProfilePreset, ProfilePresets, "ACCOUNT_COMPARISON_PROFILE_INVALID");
        var financingPreset = ValidateChoice(request.FinancingPreset, FinancingPresets, "ACCOUNT_COMPARISON_FINANCING_INVALID");
        if (await database.SavedComparisons.CountAsync(value => value.UserAccountId == actor.UserId, cancellationToken) >= 50)
        {
            throw Error(409, "ACCOUNT_COMPARISON_LIMIT", "An account can save at most 50 comparisons.");
        }
        var now = timeProvider.GetUtcNow();
        var row = new SavedComparison
        {
            UserAccountId = actor.UserId,
            Name = name,
            TrimIdsJson = JsonSerializer.Serialize(trimIds),
            RegionCode = region,
            ProfilePreset = profilePreset,
            FinancingPreset = financingPreset,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.SavedComparisons.Add(row);
        await database.SaveChangesAsync(cancellationToken);
        return MapComparison(row);
    }

    public async Task DeleteComparisonAsync(AccountActor actor, Guid id, CancellationToken cancellationToken)
    {
        var row = await database.SavedComparisons.SingleOrDefaultAsync(
            value => value.Id == id && value.UserAccountId == actor.UserId, cancellationToken)
            ?? throw Error(404, "ACCOUNT_COMPARISON_NOT_FOUND", "Saved comparison was not found.");
        database.SavedComparisons.Remove(row);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WatchlistResponse>> GetWatchlistAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var entries = await database.WatchlistEntries.AsNoTracking()
            .Where(value => value.UserAccountId == actor.UserId)
            .OrderByDescending(value => value.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        return await MapWatchlistAsync(entries, cancellationToken);
    }

    public async Task<WatchlistResponse> SaveWatchlistAsync(
        AccountActor actor,
        WatchlistRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TargetPrice < 0)
        {
            throw Error(400, "ACCOUNT_WATCHLIST_TARGET_INVALID", "Target price must be non-negative.");
        }
        if (!await database.Trims.AsNoTracking().AnyAsync(value => value.Id == request.TrimId, cancellationToken))
        {
            throw Error(404, "ACCOUNT_WATCHLIST_TRIM_NOT_FOUND", "The requested trim was not found.");
        }
        var region = await ValidateRegionAsync(request.RegionCode, cancellationToken, allowNationwide: true);
        var now = timeProvider.GetUtcNow();
        var row = await database.WatchlistEntries.SingleOrDefaultAsync(
            value => value.UserAccountId == actor.UserId && value.TrimId == request.TrimId, cancellationToken);
        if (row is null)
        {
            if (await database.WatchlistEntries.CountAsync(value => value.UserAccountId == actor.UserId, cancellationToken) >= 100)
            {
                throw Error(409, "ACCOUNT_WATCHLIST_LIMIT", "An account can watch at most 100 trims.");
            }
            row = new WatchlistEntry
            {
                UserAccountId = actor.UserId,
                TrimId = request.TrimId,
                CreatedAt = now,
            };
            database.WatchlistEntries.Add(row);
        }
        row.RegionCode = region;
        row.TargetPrice = request.TargetPrice;
        row.PriceAlerts = request.PriceAlerts;
        row.PromotionAlerts = request.PromotionAlerts;
        row.DealerOfferAlerts = request.DealerOfferAlerts;
        row.UpdatedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        return AssertSingle(await MapWatchlistAsync([row], cancellationToken));
    }

    public async Task DeleteWatchlistAsync(AccountActor actor, Guid trimId, CancellationToken cancellationToken)
    {
        var row = await database.WatchlistEntries.SingleOrDefaultAsync(
            value => value.UserAccountId == actor.UserId && value.TrimId == trimId, cancellationToken)
            ?? throw Error(404, "ACCOUNT_WATCHLIST_NOT_FOUND", "Watchlist entry was not found.");
        database.WatchlistEntries.Remove(row);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountAlertResponse>> GetAlertsAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entries = await database.WatchlistEntries.AsNoTracking()
            .Where(value => value.UserAccountId == actor.UserId
                && (value.PriceAlerts || value.PromotionAlerts || value.DealerOfferAlerts))
            .ToArrayAsync(cancellationToken);
        if (entries.Length == 0) return [];
        var trimIds = entries.Select(value => value.TrimId).Distinct().ToArray();
        var vehicles = await LoadVehiclesAsync(trimIds, cancellationToken);
        var brandIds = vehicles.Values.Select(value => value.BrandId).Distinct().ToArray();
        var prices = await database.Prices.AsNoTracking()
            .Where(value => trimIds.Contains(value.TrimId)
                && value.Status == PriceStatus.Official
                && value.Amount != null
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || now < value.EffectiveTo))
            .ToArrayAsync(cancellationToken);
        var promotions = await database.Promotions.AsNoTracking()
            .Where(value => value.Status == OfferStatus.Published
                && ((value.TrimId != null && trimIds.Contains(value.TrimId.Value))
                    || (value.BrandId != null && brandIds.Contains(value.BrandId.Value)))
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || now < value.EffectiveTo))
            .ToArrayAsync(cancellationToken);
        var offers = await (
                from offer in database.DealerOffers.AsNoTracking()
                join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
                join dealer in database.Dealers.AsNoTracking() on branch.DealerId equals dealer.Id
                where trimIds.Contains(offer.TrimId)
                    && offer.Status == OfferStatus.Published
                    && offer.EffectiveFrom <= now
                    && (offer.EffectiveTo == null || now < offer.EffectiveTo)
                select new OfferRow(offer.Id, offer.TrimId, offer.Headline, offer.SourceFactId, offer.EffectiveFrom, offer.EffectiveTo,
                    branch.ProvinceCode, dealer.Name))
            .ToArrayAsync(cancellationToken);
        var benefits = await database.DealerOfferBenefits.AsNoTracking()
            .Where(value => offers.Select(item => item.Id).Contains(value.OfferId)
                && value.IsCashEquivalent && value.CashValue != null)
            .ToArrayAsync(cancellationToken);
        var factIds = prices.Select(value => value.SourceFactId)
            .Concat(promotions.Select(value => value.SourceFactId))
            .Concat(offers.Select(value => value.SourceFactId));
        var sources = await LoadSourcesAsync(factIds, cancellationToken);

        var alerts = new List<AccountAlertResponse>();
        foreach (var entry in entries)
        {
            if (!vehicles.TryGetValue(entry.TrimId, out var vehicle)) continue;
            if (entry.PriceAlerts)
            {
                var price = SelectPrice(prices.Where(value => value.TrimId == entry.TrimId), entry.RegionCode);
                if (price is not null && AccountAlertPolicy.PriceMatches(entry.PriceAlerts, price.Amount, entry.TargetPrice))
                {
                    var threshold = entry.TargetPrice is null
                        ? "Current sourced price is available."
                        : $"Current price is at or below your target of {entry.TargetPrice:0} VND.";
                    alerts.Add(new AccountAlertResponse(
                        $"price:{price.Id}", "Price", entry.TrimId, vehicle.Label, "Price signal", threshold,
                        price.Amount, price.Currency, price.EffectiveFrom, price.EffectiveTo,
                        Source(price.SourceFactId, sources)));
                }
            }
            if (entry.PromotionAlerts)
            {
                foreach (var promotion in promotions.Where(value => AccountAlertPolicy.PromotionMatches(
                             entry.PromotionAlerts, entry.TrimId, vehicle.BrandId, value.TrimId, value.BrandId)))
                {
                    alerts.Add(new AccountAlertResponse(
                        $"promotion:{promotion.Id}", "Promotion", entry.TrimId, vehicle.Label,
                        $"{promotion.BenefitType} promotion", "A current official promotion matches this watchlist entry.",
                        promotion.Value, promotion.Currency, promotion.EffectiveFrom, promotion.EffectiveTo,
                        Source(promotion.SourceFactId, sources)));
                }
            }
            if (entry.DealerOfferAlerts)
            {
                foreach (var offer in offers.Where(value => AccountAlertPolicy.DealerOfferMatches(
                             entry.DealerOfferAlerts, entry.RegionCode, entry.TrimId, value.ProvinceCode, value.TrimId)))
                {
                    var cash = benefits.Where(value => value.OfferId == offer.Id).Sum(value => value.CashValue ?? 0);
                    alerts.Add(new AccountAlertResponse(
                        $"dealer-offer:{offer.Id}", "DealerOffer", entry.TrimId, vehicle.Label, offer.Headline,
                        $"Current offer from {offer.DealerName} in {offer.ProvinceCode}.", cash == 0 ? null : cash, "VND",
                        offer.EffectiveFrom, offer.EffectiveTo, Source(offer.SourceFactId, sources)));
                }
            }
        }
        return alerts
            .DistinctBy(value => value.Id)
            .OrderByDescending(value => value.EffectiveFrom)
            .ThenBy(value => value.Kind)
            .ToArray();
    }

    public async Task<AccountDataExportResponse> ExportAsync(AccountActor actor, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var account = await database.UserAccounts.AsNoTracking().SingleAsync(value => value.Id == actor.UserId, cancellationToken);
        var profile = await GetProfileAsync(actor, cancellationToken);
        var comparisons = await GetComparisonsAsync(actor, cancellationToken);
        var watchlist = await GetWatchlistAsync(actor, cancellationToken);
        var alerts = await GetAlertsAsync(actor, cancellationToken);
        return new AccountDataExportResponse(
            now,
            new AccountSessionResponse(account.Id, account.Email, account.DisplayName, actor.ExpiresAt,
                account.ConsentedAt, account.PrivacyPolicyVersion),
            profile,
            comparisons,
            watchlist,
            alerts);
    }

    private async Task<IReadOnlyList<WatchlistResponse>> MapWatchlistAsync(
        IReadOnlyList<WatchlistEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return [];
        var trimIds = entries.Select(value => value.TrimId).Distinct().ToArray();
        var vehicles = await LoadVehiclesAsync(trimIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var prices = await database.Prices.AsNoTracking()
            .Where(value => trimIds.Contains(value.TrimId)
                && value.Status == PriceStatus.Official
                && value.Amount != null
                && value.EffectiveFrom <= now
                && (value.EffectiveTo == null || now < value.EffectiveTo))
            .ToArrayAsync(cancellationToken);
        return entries.Where(value => vehicles.ContainsKey(value.TrimId)).Select(value =>
        {
            var vehicle = vehicles[value.TrimId];
            var price = SelectPrice(prices.Where(item => item.TrimId == value.TrimId), value.RegionCode);
            return new WatchlistResponse(
                value.Id, value.TrimId, vehicle.BrandName, vehicle.ModelName, vehicle.TrimName, value.RegionCode,
                price?.Amount, value.TargetPrice, value.PriceAlerts, value.PromotionAlerts, value.DealerOfferAlerts, value.UpdatedAt);
        }).ToArray();
    }

    private async Task<Dictionary<Guid, VehicleRow>> LoadVehiclesAsync(Guid[] trimIds, CancellationToken cancellationToken) =>
        await (
                from trim in database.Trims.AsNoTracking()
                join modelYear in database.ModelYears.AsNoTracking() on trim.ModelYearId equals modelYear.Id
                join generation in database.Generations.AsNoTracking() on modelYear.GenerationId equals generation.Id
                join model in database.Models.AsNoTracking() on generation.ModelId equals model.Id
                join brand in database.Brands.AsNoTracking() on model.BrandId equals brand.Id
                where trimIds.Contains(trim.Id)
                select new VehicleRow(trim.Id, brand.Id, brand.Name, model.Name, trim.Name))
            .ToDictionaryAsync(value => value.TrimId, cancellationToken);

    private async Task<Dictionary<Guid, AccountAlertSource>> LoadSourcesAsync(
        IEnumerable<Guid?> sourceFactIds,
        CancellationToken cancellationToken)
    {
        var ids = sourceFactIds.Where(value => value is not null).Select(value => value!.Value).Distinct().ToArray();
        return await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where ids.Contains(fact.Id)
                select new AccountAlertSource(fact.Id, source.Name, source.Url, source.AuthorityLevel.ToString(), snapshot.FetchedAt))
            .ToDictionaryAsync(value => value.SourceFactId!.Value, cancellationToken);
    }

    private async Task<string> ValidateRegionAsync(string value, CancellationToken cancellationToken, bool allowNationwide = false)
    {
        var region = value.Trim().ToUpperInvariant();
        if (allowNationwide && region == "VN") return region;
        if (region.Length is < 2 or > 20
            || !await database.Regions.AsNoTracking().AnyAsync(value => value.Code == region && value.Active, cancellationToken))
        {
            throw Error(400, "ACCOUNT_REGION_INVALID", "Region must be an active canonical Vietnam region code.");
        }
        return region;
    }

    private static Price? SelectPrice(IEnumerable<Price> candidates, string regionCode) => candidates
        .Where(value => value.RegionScope == "VN" || value.RegionScope == regionCode)
        .OrderByDescending(value => value.RegionScope == regionCode)
        .ThenBy(value => value.PriceType == PriceType.PromotionPrice ? 0 : value.PriceType == PriceType.Msrp ? 1 : 2)
        .ThenByDescending(value => value.Priority)
        .ThenByDescending(value => value.EffectiveFrom)
        .FirstOrDefault();

    private static AccountProfileResponse MapProfile(AffordabilityProfile value) => new(
        value.Id, value.Name, value.RegionCode, value.NetMonthlyIncome, value.RentHousing, value.EssentialExpenses,
        value.OtherFixedDebt, value.SavingsTarget, value.MonthlyKilometres, value.ParkingMonthly,
        value.HouseholdBaseKwh, value.Policy.ToString(), value.UpdatedAt);

    private static SavedComparisonResponse MapComparison(SavedComparison value) => new(
        value.Id, value.Name, JsonSerializer.Deserialize<Guid[]>(value.TrimIdsJson) ?? [], value.RegionCode,
        value.ProfilePreset, value.FinancingPreset, value.CreatedAt, value.UpdatedAt);

    private static AccountAlertSource Source(Guid? factId, Dictionary<Guid, AccountAlertSource> sources) =>
        factId is not null && sources.TryGetValue(factId.Value, out var source)
            ? source
            : new AccountAlertSource(factId, null, null, null, null);

    private static T AssertSingle<T>(IReadOnlyList<T> values) => values.Count == 1
        ? values[0]
        : throw new InvalidOperationException("Expected exactly one account row.");

    private static string ValidateText(string value, int minimum, int maximum, string code, string message)
    {
        var result = value.Trim();
        if (result.Length < minimum || result.Length > maximum) throw Error(400, code, message);
        return result;
    }

    private static string ValidateChoice(string value, IReadOnlyList<string> choices, string code)
    {
        var result = value.Trim();
        if (!choices.Contains(result, StringComparer.Ordinal))
        {
            throw Error(400, code, "The selected preset is not supported.");
        }
        return result;
    }

    private static AccountOperationException Error(int status, string code, string message) => new(status, code, message);

    private sealed record VehicleRow(Guid TrimId, Guid BrandId, string BrandName, string ModelName, string TrimName)
    {
        public string Label => $"{BrandName} {ModelName} · {TrimName}";
    }

    private sealed record OfferRow(
        Guid Id,
        Guid TrimId,
        string Headline,
        Guid? SourceFactId,
        DateTimeOffset EffectiveFrom,
        DateTimeOffset? EffectiveTo,
        string ProvinceCode,
        string DealerName);
}
