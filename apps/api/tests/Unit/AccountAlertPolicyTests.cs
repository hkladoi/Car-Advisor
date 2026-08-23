using VietnamCarPlatform.Domain.Accounts;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class AccountAlertPolicyTests
{
    private static readonly Guid Trim = Guid.Parse("8b31de05-bd4c-5b70-9efd-47879f5e609c");
    private static readonly Guid Brand = Guid.Parse("2ec5e3c9-c7ee-54de-857c-1562a7c247db");

    [Fact]
    public void PriceSignalsRespectOptInAndOptionalTarget()
    {
        Assert.True(AccountAlertPolicy.PriceMatches(true, 700_000_000m, null));
        Assert.True(AccountAlertPolicy.PriceMatches(true, 700_000_000m, 750_000_000m));
        Assert.False(AccountAlertPolicy.PriceMatches(true, 800_000_000m, 750_000_000m));
        Assert.False(AccountAlertPolicy.PriceMatches(false, 700_000_000m, null));
    }

    [Fact]
    public void PromotionSignalsMatchTrimOrBrandOnlyWhenEnabled()
    {
        Assert.True(AccountAlertPolicy.PromotionMatches(true, Trim, Brand, Trim, null));
        Assert.True(AccountAlertPolicy.PromotionMatches(true, Trim, Brand, null, Brand));
        Assert.False(AccountAlertPolicy.PromotionMatches(false, Trim, Brand, Trim, Brand));
        Assert.False(AccountAlertPolicy.PromotionMatches(true, Trim, Brand, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void DealerOfferSignalsRespectTrimAndProvinceScope()
    {
        Assert.True(AccountAlertPolicy.DealerOfferMatches(true, "VN", Trim, "VN-79", Trim));
        Assert.True(AccountAlertPolicy.DealerOfferMatches(true, "VN-79", Trim, "VN-79", Trim));
        Assert.False(AccountAlertPolicy.DealerOfferMatches(true, "VN-01", Trim, "VN-79", Trim));
        Assert.False(AccountAlertPolicy.DealerOfferMatches(false, "VN", Trim, "VN-79", Trim));
    }
}
