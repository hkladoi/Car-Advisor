using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Rules;

namespace VietnamCarPlatform.Domain.Affordability;

public sealed record DealerFinancingBenefitCandidate(
    Guid OfferId,
    Guid BenefitId,
    BenefitType Type,
    decimal Amount,
    bool IsCashEquivalent,
    string? OfferCombinabilityGroup,
    string? ExclusivityGroup,
    string ConditionsJson,
    string? Note);

public sealed record FinancingBenefitContext(
    PurchaseMethod PurchaseMethod,
    LoanRepaymentMethod RepaymentMethod,
    PurchaseFundingSource FundingSource,
    decimal TradeInNetValue,
    decimal PreliminaryPrincipal,
    decimal AnnualInterestRate,
    int TermMonths,
    decimal DownPaymentPercent,
    RegistrationRuleContext VehicleContext);

public sealed record AppliedDealerFinancingCredit(
    Guid OfferId,
    Guid BenefitId,
    BenefitType Type,
    decimal Amount,
    string? Note);

public static class DealerFinancingBenefitEvaluator
{
    public static IReadOnlyList<AppliedDealerFinancingCredit> Evaluate(
        IEnumerable<DealerFinancingBenefitCandidate> candidates,
        FinancingBenefitContext context)
    {
        var attributes = new Dictionary<string, object?>(context.VehicleContext.Attributes ?? new Dictionary<string, object?>(), StringComparer.OrdinalIgnoreCase)
        {
            ["purchaseMethod"] = context.PurchaseMethod.ToString(),
            ["repaymentMethod"] = context.RepaymentMethod.ToString(),
            ["fundingSource"] = context.FundingSource.ToString(),
            ["hasTradeIn"] = context.TradeInNetValue > 0,
            ["tradeInNetValue"] = context.TradeInNetValue,
            ["loanPrincipal"] = context.PreliminaryPrincipal,
            ["annualInterestRate"] = context.AnnualInterestRate,
            ["termMonths"] = context.TermMonths,
            ["downPaymentPercent"] = context.DownPaymentPercent,
        };
        var ruleContext = context.VehicleContext with { Attributes = attributes };
        var eligible = candidates
            .Where(candidate => candidate.IsCashEquivalent && candidate.Amount > 0)
            .Where(candidate => candidate.Type switch
            {
                BenefitType.FinancingBonus => context.PurchaseMethod == PurchaseMethod.Loan,
                BenefitType.TradeInBonus => context.TradeInNetValue > 0,
                _ => false,
            })
            .Where(candidate => RegistrationRuleEvaluator.Matches(candidate.ConditionsJson, ruleContext))
            .ToArray();
        var compatible = eligible
            .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.OfferCombinabilityGroup)
                ? $"offer:{candidate.OfferId}"
                : $"offer-exclusive:{candidate.OfferCombinabilityGroup}")
            .SelectMany(group => group
                .GroupBy(candidate => candidate.OfferId)
                .OrderByDescending(offer => offer.Sum(candidate => candidate.Amount))
                .First());
        return compatible
            .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.ExclusivityGroup)
                ? $"benefit:{candidate.BenefitId}"
                : $"exclusive:{candidate.ExclusivityGroup}")
            .Select(group => group.OrderByDescending(candidate => candidate.Amount).First())
            .Select(candidate => new AppliedDealerFinancingCredit(
                candidate.OfferId,
                candidate.BenefitId,
                candidate.Type,
                candidate.Amount,
                candidate.Note))
            .ToArray();
    }
}
