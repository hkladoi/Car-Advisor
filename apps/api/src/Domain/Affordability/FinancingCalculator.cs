using VietnamCarPlatform.Domain.Commerce;

namespace VietnamCarPlatform.Domain.Affordability;

public sealed record FinancingCalculationInput(
    decimal AcquisitionCost,
    decimal AvailableCash,
    decimal FamilyContribution,
    decimal TradeInNetValue,
    decimal OtherUpfrontCredits,
    decimal? DownPaymentAmount,
    decimal? DownPaymentPercent,
    decimal AnnualInterestRate,
    int TermMonths,
    decimal UpfrontFees,
    PurchaseFundingSource FundingSource,
    PurchaseMethod PurchaseMethod,
    LoanRepaymentMethod RepaymentMethod);

public sealed record FinancingCalculationResult(
    string PurchaseStatus,
    string FinancingStatus,
    decimal AcquisitionCost,
    decimal ExternalContribution,
    decimal TradeInNetValue,
    decimal OtherUpfrontCredits,
    decimal FinancedBasis,
    decimal DownPayment,
    decimal UpfrontCashRequired,
    decimal AvailableCash,
    decimal CashShortfall,
    decimal LoanPrincipal,
    decimal FirstPayment,
    decimal AveragePayment,
    decimal LastPayment,
    decimal MonthlyPaymentForCommitment,
    decimal TotalInterest,
    decimal TotalLoanRepayment,
    string Currency);

public static class FinancingCalculator
{
    public static FinancingCalculationResult Calculate(FinancingCalculationInput input)
    {
        Validate(input);
        var acquisition = Round(input.AcquisitionCost);
        var external = Round(input.FamilyContribution);
        var tradeIn = Round(input.TradeInNetValue);
        var credits = Round(input.OtherUpfrontCredits);
        var remaining = Math.Max(0, acquisition - external - tradeIn - credits);
        var externallyFunded = input.FundingSource == PurchaseFundingSource.FamilyFunded
            && external + tradeIn + credits >= acquisition;
        if (externallyFunded)
        {
            return Result(
                "ExternallyFunded",
                "NotApplicable",
                input,
                acquisition,
                external,
                tradeIn,
                credits,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        if (input.PurchaseMethod == PurchaseMethod.Cash)
        {
            var required = Round(remaining + input.UpfrontFees);
            var shortfall = Math.Max(0, required - input.AvailableCash);
            return Result(
                shortfall == 0 ? "Pass" : "Fail",
                "NotApplicable",
                input,
                acquisition,
                external,
                tradeIn,
                credits,
                remaining,
                remaining,
                required,
                shortfall,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var downPayment = input.DownPaymentAmount
            ?? remaining * (input.DownPaymentPercent ?? 0);
        downPayment = Math.Min(remaining, Round(downPayment));
        var upfront = Round(downPayment + input.UpfrontFees);
        var cashShortfall = Math.Max(0, upfront - input.AvailableCash);
        var principal = Round(Math.Max(0, remaining - downPayment));
        if (principal == 0)
        {
            return Result(
                cashShortfall == 0 ? "Pass" : "Fail",
                "NotApplicable",
                input,
                acquisition,
                external,
                tradeIn,
                credits,
                remaining,
                downPayment,
                upfront,
                cashShortfall,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var monthlyRate = input.AnnualInterestRate / 12;
        decimal first;
        decimal average;
        decimal last;
        decimal totalInterest;
        if (input.RepaymentMethod == LoanRepaymentMethod.Annuity)
        {
            var payment = monthlyRate == 0
                ? principal / input.TermMonths
                : Annuity(principal, monthlyRate, input.TermMonths);
            var total = payment * input.TermMonths;
            first = average = last = Round(payment);
            totalInterest = Round(total - principal);
        }
        else
        {
            var principalPayment = principal / input.TermMonths;
            first = Round(principalPayment + principal * monthlyRate);
            last = Round(principalPayment + principalPayment * monthlyRate);
            totalInterest = Round(principal * monthlyRate * (input.TermMonths + 1) / 2);
            average = Round((principal + totalInterest) / input.TermMonths);
        }

        return Result(
            cashShortfall == 0 ? "Pass" : "Fail",
            "Applicable",
            input,
            acquisition,
            external,
            tradeIn,
            credits,
            remaining,
            downPayment,
            upfront,
            cashShortfall,
            principal,
            first,
            average,
            last,
            input.RepaymentMethod == LoanRepaymentMethod.ReducingBalance ? first : average,
            totalInterest,
            principal + totalInterest);
    }

    private static decimal Annuity(decimal principal, decimal monthlyRate, int termMonths)
    {
        var factor = (decimal)Math.Pow((double)(1 + monthlyRate), termMonths);
        return principal * monthlyRate * factor / (factor - 1);
    }

    private static FinancingCalculationResult Result(
        string purchaseStatus,
        string financingStatus,
        FinancingCalculationInput input,
        decimal acquisition,
        decimal external,
        decimal tradeIn,
        decimal credits,
        decimal basis,
        decimal downPayment,
        decimal upfront,
        decimal shortfall,
        decimal principal,
        decimal first,
        decimal average,
        decimal last,
        decimal commitment,
        decimal interest,
        decimal repayment) => new(
            purchaseStatus,
            financingStatus,
            acquisition,
            external,
            tradeIn,
            credits,
            Round(basis),
            Round(downPayment),
            Round(upfront),
            Round(input.AvailableCash),
            Round(shortfall),
            Round(principal),
            Round(first),
            Round(average),
            Round(last),
            Round(commitment),
            Round(interest),
            Round(repayment),
            "VND");

    private static void Validate(FinancingCalculationInput input)
    {
        var amounts = new[]
        {
            input.AcquisitionCost,
            input.AvailableCash,
            input.FamilyContribution,
            input.TradeInNetValue,
            input.OtherUpfrontCredits,
            input.DownPaymentAmount ?? 0,
            input.DownPaymentPercent ?? 0,
            input.AnnualInterestRate,
            input.UpfrontFees,
        };
        if (amounts.Any(value => value < 0)
            || input.DownPaymentPercent > 1
            || (input.DownPaymentAmount is not null && input.DownPaymentPercent is not null))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Financing amounts and rates are invalid; choose a down-payment amount or percentage, not both.");
        }
        if (input.PurchaseMethod == PurchaseMethod.Loan && input.TermMonths is < 1 or > 480)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Loan term must be between 1 and 480 months.");
        }
        if (input.PurchaseMethod == PurchaseMethod.Cash && input.TermMonths != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Cash purchase cannot have a loan term.");
        }
    }

    private static decimal Round(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
