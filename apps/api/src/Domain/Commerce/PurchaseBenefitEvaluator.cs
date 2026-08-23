namespace VietnamCarPlatform.Domain.Commerce;

public sealed record PurchaseBenefit(
    BenefitType Type,
    decimal? CashValue,
    decimal? StatedValue,
    bool IsCashEquivalent,
    string Origin,
    Guid OriginId,
    string? Note = null);

public sealed record PurchaseBenefitSummary(
    decimal CashPurchaseReduction,
    decimal RegistrationFeeSupport,
    decimal FirstRegistrationTaxSupport,
    IReadOnlyList<PurchaseBenefit> CashBenefits,
    IReadOnlyList<PurchaseBenefit> NonCashBenefits);

public static class PurchaseBenefitEvaluator
{
    public static PurchaseBenefitSummary Summarize(IEnumerable<PurchaseBenefit> benefits)
    {
        var materialized = benefits.ToArray();
        var cashBenefits = materialized
            .Where(benefit => benefit.Type == BenefitType.CashDiscount && benefit.IsCashEquivalent && benefit.CashValue > 0)
            .ToArray();
        var feeSupport = materialized
            .Where(benefit => benefit.Type == BenefitType.RegistrationFeeSupport)
            .Sum(benefit => benefit.CashValue ?? benefit.StatedValue ?? 0);
        var taxSupport = materialized
            .Where(benefit => benefit.Type == BenefitType.FirstRegistrationTaxSupport)
            .Sum(benefit => benefit.CashValue ?? benefit.StatedValue ?? 0);
        var nonCash = materialized
            .Where(benefit => !cashBenefits.Contains(benefit)
                && benefit.Type is not BenefitType.RegistrationFeeSupport
                && benefit.Type is not BenefitType.FirstRegistrationTaxSupport)
            .ToArray();

        return new PurchaseBenefitSummary(
            cashBenefits.Sum(benefit => benefit.CashValue ?? 0),
            feeSupport,
            taxSupport,
            cashBenefits,
            nonCash);
    }
}
