using VietnamCarPlatform.Api.Features.Pricing;
using VietnamCarPlatform.Domain.Commerce;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class HistoryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TwelveMonthClaimIsWithheldWhenHistoryIsTooShort()
    {
        var timeline = new[]
        {
            PriceEvent(Now.AddDays(-30), 700_000_000m),
            PriceEvent(Now.AddDays(-1), 680_000_000m, current: true),
        };

        var insight = HistoryService.EvaluateRange(timeline, Now);

        Assert.False(insight.Available);
        Assert.Equal("INSUFFICIENT_12_MONTH_HISTORY", insight.ReasonCode);
        Assert.Equal(2, insight.ObservationCount);
        Assert.Null(insight.TwelveMonthMinimum);
        Assert.Null(insight.Position);
    }

    [Fact]
    public void TwelveMonthLowIsShownOnlyAfterThreeDatesAcrossNinetyDays()
    {
        var timeline = new[]
        {
            PriceEvent(Now.AddDays(-220), 720_000_000m),
            PriceEvent(Now.AddDays(-120), 700_000_000m),
            PriceEvent(Now.AddDays(-1), 680_000_000m, current: true),
            // Benefits are deliberately excluded from cash-price range math.
            PriceEvent(Now.AddDays(-2), 100_000_000m, current: true, valueKind: "CashBenefit"),
        };

        var insight = HistoryService.EvaluateRange(timeline, Now);

        Assert.True(insight.Available);
        Assert.Equal("ENOUGH_HISTORY", insight.ReasonCode);
        Assert.Equal(680_000_000m, insight.TwelveMonthMinimum);
        Assert.Equal(720_000_000m, insight.TwelveMonthMaximum);
        Assert.Equal("At12MonthLow", insight.Position);
        Assert.Equal(3, insight.ObservationCount);
    }

    [Fact]
    public void HistoricalObservationsWithoutCurrentPriceDoNotProduceAClaim()
    {
        var insight = HistoryService.EvaluateRange(
        [
            PriceEvent(Now.AddDays(-220), 720_000_000m),
            PriceEvent(Now.AddDays(-120), 700_000_000m),
            PriceEvent(Now.AddDays(-20), 680_000_000m),
        ], Now);

        Assert.False(insight.Available);
        Assert.Equal("NO_CURRENT_CASH_PRICE", insight.ReasonCode);
    }

    [Fact]
    public void DealerCashReductionExcludesGiftsAndDoesNotStackExclusiveBenefits()
    {
        var reduction = HistoryService.MaximumEligibleCashReduction(
        [
            Benefit(10_000_000m),
            Benefit(15_000_000m, "either-or"),
            Benefit(12_000_000m, "either-or"),
            Benefit(null, stated: 50_000_000m, cashEquivalent: false),
        ]);

        Assert.Equal(25_000_000m, reduction);
    }

    private static PriceTimelineEvent PriceEvent(
        DateTimeOffset at,
        decimal amount,
        bool current = false,
        string valueKind = "CashPrice") => new(
            Guid.NewGuid(),
            "Msrp",
            valueKind,
            amount,
            "VND",
            "Official",
            "VN",
            "Official MSRP",
            at,
            null,
            current,
            false,
            "SourceFact",
            null);

    private static DealerOfferBenefit Benefit(
        decimal? cash,
        string? exclusivity = null,
        decimal? stated = null,
        bool cashEquivalent = true) => new()
        {
            CashValue = cash,
            StatedValue = stated,
            IsCashEquivalent = cashEquivalent,
            ExclusivityGroup = exclusivity,
        };
}
