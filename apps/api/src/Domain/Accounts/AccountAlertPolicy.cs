namespace VietnamCarPlatform.Domain.Accounts;

public static class AccountAlertPolicy
{
    public static bool PriceMatches(bool enabled, decimal? currentPrice, decimal? targetPrice) =>
        enabled && currentPrice is not null && (targetPrice is null || currentPrice <= targetPrice);

    public static bool PromotionMatches(
        bool enabled,
        Guid watchedTrimId,
        Guid watchedBrandId,
        Guid? promotionTrimId,
        Guid? promotionBrandId) =>
        enabled && (promotionTrimId == watchedTrimId || promotionBrandId == watchedBrandId);

    public static bool DealerOfferMatches(bool enabled, string watchedRegion, Guid watchedTrimId, string offerRegion, Guid offerTrimId) =>
        enabled && watchedTrimId == offerTrimId
            && (string.Equals(watchedRegion, "VN", StringComparison.Ordinal)
                || string.Equals(watchedRegion, offerRegion, StringComparison.Ordinal));
}
