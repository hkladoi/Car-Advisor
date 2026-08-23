using VietnamCarPlatform.Domain.Admin;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class DealerOfferQualityEvaluatorTests
{
    [Fact]
    public void PublishedExpiredOfferWithConflictsProducesEveryQaClass()
    {
        var offer = new DealerOfferQualityInput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "VN-01",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            "Published",
            "{\"provinceCode\":\"VN-79\"}",
            false,
            [
                new(Guid.NewGuid(), "CashDiscount", "cash", 10_000_000, 10_000_000),
                new(Guid.NewGuid(), "CashDiscount", "cash", 5_000_000, 5_000_000),
            ]);

        var issues = DealerOfferQualityEvaluator.Evaluate(offer, new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains(issues, issue => issue.Code == "DEALER_OFFER_EXPIRED");
        Assert.Contains(issues, issue => issue.Code == "DEALER_OFFER_PROVENANCE_MISSING");
        Assert.Contains(issues, issue => issue.Code == "DEALER_OFFER_DUPLICATE_BENEFIT");
        Assert.Contains(issues, issue => issue.Code == "DEALER_OFFER_EXCLUSIVITY_CONFLICT");
        Assert.Contains(issues, issue => issue.Code == "DEALER_OFFER_REGION_BRANCH_MISMATCH");
    }

    [Fact]
    public void CurrentSourcedOfferWithoutDuplicateBenefitsIsClean()
    {
        var offer = new DealerOfferQualityInput(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "VN-01",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            "Published",
            "{\"provinceCode\":\"VN-01\"}",
            true,
            [new(Guid.NewGuid(), "CashDiscount", null, 10_000_000, 10_000_000)]);

        Assert.Empty(DealerOfferQualityEvaluator.Evaluate(offer, DateTimeOffset.UtcNow));
    }
}
