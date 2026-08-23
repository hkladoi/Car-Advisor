using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Rules;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class DealerFinancingBenefitEvaluatorTests
{
    [Fact]
    public void FinancingBonusCannotApplyWhenFinancingConditionIsFalse()
    {
        var candidate = new DealerFinancingBenefitCandidate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BenefitType.FinancingBonus,
            20_000_000,
            true,
            null,
            null,
            "{\"condition\":{\"field\":\"termMonths\",\"operator\":\"gte\",\"value\":48}}",
            null);
        var context = Context(termMonths: 36);

        Assert.Empty(DealerFinancingBenefitEvaluator.Evaluate([candidate], context));
        Assert.Single(DealerFinancingBenefitEvaluator.Evaluate([candidate], context with { TermMonths = 60 }));
        Assert.Empty(DealerFinancingBenefitEvaluator.Evaluate([candidate], context with { PurchaseMethod = PurchaseMethod.Cash, TermMonths = 60 }));
    }

    private static FinancingBenefitContext Context(int termMonths) => new(
        PurchaseMethod.Loan,
        LoanRepaymentMethod.Annuity,
        PurchaseFundingSource.SelfFunded,
        0,
        600_000_000,
        0.12m,
        termMonths,
        0.2m,
        new RegistrationRuleContext("VN-01", "I", "PassengerCar", 5, "Bev", "Individual", true, 12, 700_000_000));
}
