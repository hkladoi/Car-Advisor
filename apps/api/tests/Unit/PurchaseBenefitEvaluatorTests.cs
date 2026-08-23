using VietnamCarPlatform.Domain.Commerce;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class PurchaseBenefitEvaluatorTests
{
    [Fact]
    public void GiftsAndFeeSupportNeverReduceEffectiveCashPurchasePrice()
    {
        var origin = Guid.NewGuid();
        var summary = PurchaseBenefitEvaluator.Summarize(
        [
            new(BenefitType.CashDiscount, 10_000_000, 10_000_000, true, "DealerOffer", origin),
            new(BenefitType.AccessoryPackage, null, 20_000_000, false, "DealerOffer", origin),
            new(BenefitType.InsuranceGift, null, 5_000_000, false, "DealerOffer", origin),
            new(BenefitType.RegistrationFeeSupport, 2_000_000, 2_000_000, true, "DealerOffer", origin),
        ]);

        Assert.Equal(10_000_000, summary.CashPurchaseReduction);
        Assert.Equal(2_000_000, summary.RegistrationFeeSupport);
        Assert.Equal(2, summary.NonCashBenefits.Count);
    }
}
