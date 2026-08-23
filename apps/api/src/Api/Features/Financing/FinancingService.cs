using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietnamCarPlatform.Api.Features.Affordability;
using VietnamCarPlatform.Api.Features.Registration;
using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Domain.Rules;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Financing;

public interface IFinancingService
{
    Task<FinancingCalculationResponse> CalculateAsync(FinancingCalculationRequest request, CancellationToken cancellationToken);
}

public sealed class FinancingService(
    AppDbContext database,
    IAffordabilityService affordabilityService,
    IRegistrationService registrationService,
    IOptions<AffordabilityOptions> configuredOptions,
    TimeProvider timeProvider) : IFinancingService
{
    private readonly AffordabilityOptions options = configuredOptions.Value;

    public async Task<FinancingCalculationResponse> CalculateAsync(
        FinancingCalculationRequest request,
        CancellationToken cancellationToken)
    {
        var parsed = Validate(request);
        var instant = (request.CalculationDate ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var warnings = new List<string>();

        try
        {
            var ownership = await affordabilityService.CalculateOwnershipAsync(new OwnershipCalculationRequest
            {
                TrimId = request.TrimId,
                ProvinceCode = request.ProvinceCode,
                CalculationDate = instant,
                Expenses = request.Expenses,
                Energy = request.Energy,
            }, cancellationToken);
            var ownershipAffordability = AffordabilityEvaluator.Evaluate(new AffordabilityEvaluationInput(
                request.NetMonthlyIncome,
                request.RentHousing,
                request.EssentialExpenses,
                request.OtherFixedDebt,
                request.SavingsTarget,
                request.MaximumMonthlyVehicleSpend,
                options.Thresholds(parsed.Policy),
                ownership.Result));
            var interestRate = await ResolveInterestRateAsync(request.Purchase, parsed.PurchaseMethod, warnings, cancellationToken);

            var baseline = await registrationService.CalculateAsync(RegistrationRequest(request, instant, [], null), cancellationToken);
            var provisional = CreateFinancingInput(request.Purchase, parsed, baseline.Result.OnRoadPrice, interestRate.AnnualInterestRate, 0);
            var provisionalResult = FinancingCalculator.Calculate(provisional);
            var scenarioAttributes = ScenarioAttributes(request.Purchase, parsed, provisionalResult, provisional);
            var onRoad = request.Purchase.SelectedDealerOfferIds.Count == 0
                ? baseline
                : await registrationService.CalculateAsync(
                    RegistrationRequest(request, instant, request.Purchase.SelectedDealerOfferIds, scenarioAttributes),
                    cancellationToken);

            var dealerCredits = await EvaluateDealerCreditsAsync(
                request,
                parsed,
                interestRate.AnnualInterestRate,
                onRoad,
                instant,
                cancellationToken);
            var creditTotal = dealerCredits.Sum(credit => credit.Amount);
            var financingInput = CreateFinancingInput(
                request.Purchase,
                parsed,
                onRoad.Result.OnRoadPrice,
                interestRate.AnnualInterestRate,
                creditTotal);
            var financing = FinancingCalculator.Calculate(financingInput);
            if (parsed.FundingSource == PurchaseFundingSource.FamilyFunded
                && financing.PurchaseStatus != "ExternallyFunded")
            {
                throw new FinancingCalculationException(
                    StatusCodes.Status422UnprocessableEntity,
                    "FAMILY_FUNDING_INCOMPLETE",
                    "FamilyFunded is reserved for an outright externally funded purchase; use Mixed when the buyer also contributes cash or takes a loan.");
            }

            var purchaseThresholds = options.PurchaseThresholds(parsed.Policy);
            var purchaseCashflow = PurchaseCashflowEvaluator.Evaluate(new PurchaseCashflowInput(
                request.NetMonthlyIncome,
                request.RentHousing,
                request.EssentialExpenses,
                request.OtherFixedDebt,
                request.SavingsTarget,
                ownership.Result.NormalizedMonthlyCost,
                financing.MonthlyPaymentForCommitment,
                purchaseThresholds));
            var purchaseRating = PurchaseRating(financing, purchaseCashflow, ownershipAffordability);
            warnings.AddRange(onRoad.Warnings);
            if (request.Purchase.SelectedDealerOfferIds.Count > 0 && dealerCredits.Count == 0)
            {
                warnings.Add("NO_ELIGIBLE_FINANCING_CREDIT: selected dealer offers supplied no source-backed financing or trade-in cash credit for this scenario.");
            }

            return new FinancingCalculationResponse(
                parsed.Policy.ToString(),
                purchaseRating,
                new FinancingProfileSummary(
                    request.NetMonthlyIncome,
                    request.RentHousing,
                    request.EssentialExpenses,
                    request.OtherFixedDebt,
                    request.SavingsTarget,
                    request.NetMonthlyIncome - request.RentHousing - request.EssentialExpenses - request.OtherFixedDebt - request.SavingsTarget,
                    "VND"),
                ownership,
                ownershipAffordability,
                onRoad,
                financing,
                purchaseThresholds,
                purchaseCashflow,
                interestRate,
                dealerCredits,
                [
                    "Acquisition cash and loan payments are evaluated separately from OperatingOwnershipCost.",
                    "Reducing-balance affordability uses the first payment; annuity affordability uses the fixed payment.",
                    "Dealer financing and trade-in credits apply only when an active selected offer and all machine-readable conditions match.",
                    "A user-entered interest rate is an assumption unless an official source fact is selected.",
                    "This scenario is an estimate for comparison, not a credit approval, quote, or financial advice.",
                ],
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                timeProvider.GetUtcNow());
        }
        catch (OwnershipCalculationException exception)
        {
            throw new FinancingCalculationException(exception.StatusCode, exception.Code, exception.Message);
        }
        catch (RegistrationCalculationException exception)
        {
            throw new FinancingCalculationException(exception.StatusCode, exception.Code, exception.Message);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "FINANCING_INPUT_INVALID", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new FinancingCalculationException(StatusCodes.Status422UnprocessableEntity, "FINANCING_RULE_INVALID", exception.Message);
        }
    }

    private async Task<IReadOnlyList<AppliedDealerFinancingCreditResponse>> EvaluateDealerCreditsAsync(
        FinancingCalculationRequest request,
        ParsedRequest parsed,
        decimal annualInterestRate,
        OnRoadCalculationResponse onRoad,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        if (request.Purchase.SelectedDealerOfferIds.Count == 0)
        {
            return [];
        }

        var selected = request.Purchase.SelectedDealerOfferIds.Distinct().ToArray();
        var rows = await (
                from offer in database.DealerOffers.AsNoTracking()
                join branch in database.DealerBranches.AsNoTracking() on offer.BranchId equals branch.Id
                join benefit in database.DealerOfferBenefits.AsNoTracking() on offer.Id equals benefit.OfferId
                where selected.Contains(offer.Id)
                    && offer.TrimId == request.TrimId
                    && offer.Status == OfferStatus.Published
                    && branch.ProvinceCode == request.ProvinceCode
                    && offer.EffectiveFrom <= instant
                    && (offer.EffectiveTo == null || offer.EffectiveTo > instant)
                select new
                {
                    Offer = offer,
                    Benefit = benefit,
                })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var preCredit = CreateFinancingInput(request.Purchase, parsed, onRoad.Result.OnRoadPrice, annualInterestRate, 0);
        var preliminary = FinancingCalculator.Calculate(preCredit);
        var remainingBeforeDown = Math.Max(
            0,
            onRoad.Result.OnRoadPrice - request.Purchase.FamilyContribution - request.Purchase.TradeInNetValue);
        var downPercent = remainingBeforeDown == 0
            ? 0
            : preliminary.DownPayment / remainingBeforeDown;
        var ruleContext = new RegistrationRuleContext(
            onRoad.Region.Code,
            onRoad.Region.AreaClass,
            "PassengerCar",
            onRoad.Vehicle.Seats,
            onRoad.Vehicle.Powertrain,
            "Individual",
            request.Expenses.FirstInspectionExempt,
            12,
            onRoad.Result.EffectiveCashPurchasePrice);
        var applied = DealerFinancingBenefitEvaluator.Evaluate(
            rows.Select(row => new DealerFinancingBenefitCandidate(
                row.Offer.Id,
                row.Benefit.Id,
                row.Benefit.Type,
                row.Benefit.CashValue ?? 0,
                row.Benefit.IsCashEquivalent,
                row.Offer.CombinabilityGroup,
                row.Benefit.ExclusivityGroup,
                row.Offer.ConditionsJson,
                row.Benefit.Note)),
            new FinancingBenefitContext(
                parsed.PurchaseMethod,
                parsed.RepaymentMethod,
                parsed.FundingSource,
                request.Purchase.TradeInNetValue,
                preliminary.LoanPrincipal,
                annualInterestRate,
                parsed.PurchaseMethod == PurchaseMethod.Loan ? request.Purchase.TermMonths : 0,
                downPercent,
                ruleContext));
        var offerSourceIds = rows
            .Where(row => row.Offer.SourceFactId is not null)
            .Select(row => row.Offer.SourceFactId!.Value)
            .Distinct()
            .ToArray();
        var sources = await LoadSourceReferencesAsync(offerSourceIds, cancellationToken);
        var metadata = rows
            .GroupBy(row => row.Offer.Id)
            .ToDictionary(group => group.Key, group => new
            {
                group.First().Offer.Headline,
                group.First().Offer.SourceFactId,
            });
        return applied.Select(credit =>
        {
            var offer = metadata[credit.OfferId];
            return new AppliedDealerFinancingCreditResponse(
                credit.OfferId,
                credit.BenefitId,
                offer.Headline,
                credit.Type.ToString(),
                credit.Amount,
                "VND",
                credit.Note,
                offer.SourceFactId is Guid sourceFactId ? sources.GetValueOrDefault(sourceFactId) : null);
        }).ToArray();
    }

    private async Task<InterestRateReference> ResolveInterestRateAsync(
        PurchaseFundingRequest purchase,
        PurchaseMethod purchaseMethod,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (purchaseMethod == PurchaseMethod.Cash)
        {
            return new InterestRateReference(0, "NotApplicable", null, null, null);
        }
        if (purchase.InterestRateSourceFactId is not Guid sourceFactId)
        {
            return new InterestRateReference(
                purchase.AnnualInterestRate!.Value,
                "UserInput",
                null,
                purchase.AnnualInterestRate.Value.ToString(CultureInfo.InvariantCulture),
                null);
        }

        var row = await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where fact.Id == sourceFactId
                    && fact.Status == FactStatus.Official
                    && source.Active
                select new { Fact = fact, Snapshot = snapshot, Source = source })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new FinancingCalculationException(
                StatusCodes.Status400BadRequest,
                "INTEREST_RATE_SOURCE_INVALID",
                "InterestRateSourceFactId must identify an active official source fact.");
        if (!row.Fact.FieldPath.Contains("rate", StringComparison.OrdinalIgnoreCase)
            && !row.Fact.FieldPath.Contains("interest", StringComparison.OrdinalIgnoreCase))
        {
            throw new FinancingCalculationException(
                StatusCodes.Status400BadRequest,
                "INTEREST_RATE_SOURCE_INVALID",
                "The selected source fact is not an interest-rate field.");
        }

        var raw = row.Fact.NormalizedValue ?? row.Fact.RawValue;
        var rate = ParseRate(raw);
        if (purchase.AnnualInterestRate is not null && purchase.AnnualInterestRate.Value != rate)
        {
            warnings.Add("RATE_INPUT_OVERRIDDEN: the official selected source fact takes precedence over the user-entered rate.");
        }
        return new InterestRateReference(
            rate,
            "VerifiedSource",
            row.Fact.FieldPath,
            row.Fact.RawValue,
            ToSourceReference(row.Fact.Id, row.Snapshot, row.Source, row.Fact.Status, row.Fact.Confidence));
    }

    private async Task<Dictionary<Guid, RuleSourceReference>> LoadSourceReferencesAsync(
        IReadOnlyCollection<Guid> sourceFactIds,
        CancellationToken cancellationToken)
    {
        if (sourceFactIds.Count == 0)
        {
            return [];
        }
        var rows = await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where sourceFactIds.Contains(fact.Id)
                select new { Fact = fact, Snapshot = snapshot, Source = source })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.Fact.Id,
            row => ToSourceReference(row.Fact.Id, row.Snapshot, row.Source, row.Fact.Status, row.Fact.Confidence));
    }

    private static RuleSourceReference ToSourceReference(
        Guid sourceFactId,
        SourceSnapshot snapshot,
        Source source,
        FactStatus factStatus,
        ConfidenceLevel confidence) => new(
            sourceFactId,
            source.Id,
            source.Name,
            source.Url,
            source.AuthorityLevel.ToString(),
            source.ContentType.ToString(),
            snapshot.FetchedAt,
            snapshot.ContentHash,
            factStatus.ToString(),
            confidence.ToString());

    private static OnRoadCalculationRequest RegistrationRequest(
        FinancingCalculationRequest request,
        DateTimeOffset instant,
        IReadOnlyList<Guid> offerIds,
        IReadOnlyDictionary<string, object?>? scenarioAttributes) => new()
        {
            TrimId = request.TrimId,
            ProvinceCode = request.ProvinceCode,
            CalculationDate = instant,
            BuyerType = "Individual",
            VehicleType = "PassengerCar",
            FirstInspectionExempt = request.Expenses.FirstInspectionExempt,
            RoadUsageMonths = 12,
            SelectedOfferIds = offerIds,
            ScenarioAttributes = scenarioAttributes,
        };

    private static FinancingCalculationInput CreateFinancingInput(
        PurchaseFundingRequest purchase,
        ParsedRequest parsed,
        decimal acquisitionCost,
        decimal annualInterestRate,
        decimal credits) => new(
            acquisitionCost,
            purchase.AvailableCash,
            purchase.FamilyContribution,
            purchase.TradeInNetValue,
            credits,
            parsed.PurchaseMethod == PurchaseMethod.Loan ? purchase.DownPaymentAmount : null,
            parsed.PurchaseMethod == PurchaseMethod.Loan ? purchase.DownPaymentPercent : null,
            parsed.PurchaseMethod == PurchaseMethod.Loan ? annualInterestRate : 0,
            parsed.PurchaseMethod == PurchaseMethod.Loan ? purchase.TermMonths : 0,
            parsed.PurchaseMethod == PurchaseMethod.Loan ? purchase.BankFees + purchase.LoanInsuranceUpfront : 0,
            parsed.FundingSource,
            parsed.PurchaseMethod,
            parsed.RepaymentMethod);

    private static Dictionary<string, object?> ScenarioAttributes(
        PurchaseFundingRequest purchase,
        ParsedRequest parsed,
        FinancingCalculationResult result,
        FinancingCalculationInput input) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["purchaseMethod"] = parsed.PurchaseMethod.ToString(),
            ["repaymentMethod"] = parsed.RepaymentMethod.ToString(),
            ["fundingSource"] = parsed.FundingSource.ToString(),
            ["hasTradeIn"] = purchase.TradeInNetValue > 0,
            ["tradeInNetValue"] = purchase.TradeInNetValue,
            ["loanPrincipal"] = result.LoanPrincipal,
            ["annualInterestRate"] = input.AnnualInterestRate,
            ["termMonths"] = input.TermMonths,
            ["downPaymentPercent"] = result.FinancedBasis == 0 ? 0 : result.DownPayment / result.FinancedBasis,
        };

    private static string PurchaseRating(
        FinancingCalculationResult financing,
        PurchaseCashflowResult cashflow,
        AffordabilityEvaluationResult ownership) => financing.PurchaseStatus switch
        {
            "ExternallyFunded" => "ExternallyFunded",
            "Fail" => "Fail",
            _ when cashflow.Rating == "Fail" || !ownership.Eligible => "Fail",
            _ when cashflow.Rating == "Warn" || ownership.Rating == "Watch" => "Warn",
            _ => "Pass",
        };

    private static decimal ParseRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "INTEREST_RATE_SOURCE_VALUE_INVALID", "The interest-rate source fact has no numeric value.");
        }
        var normalized = value.Trim().Replace("%", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "INTEREST_RATE_SOURCE_VALUE_INVALID", "The interest-rate source fact is not numeric.");
        }
        if (value.Contains('%') || rate > 1)
        {
            rate /= 100;
        }
        if (rate is < 0 or > 1)
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "INTEREST_RATE_SOURCE_VALUE_INVALID", "The annual interest rate must be between 0% and 100%.");
        }
        return rate;
    }

    private static ParsedRequest Validate(FinancingCalculationRequest request)
    {
        if (request.TrimId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ProvinceCode)
            || request.NetMonthlyIncome <= 0
            || new decimal?[]
            {
                request.RentHousing,
                request.EssentialExpenses,
                request.OtherFixedDebt,
                request.SavingsTarget,
                request.MaximumMonthlyVehicleSpend,
                request.Purchase.AvailableCash,
                request.Purchase.FamilyContribution,
                request.Purchase.TradeInNetValue,
                request.Purchase.DownPaymentAmount,
                request.Purchase.DownPaymentPercent,
                request.Purchase.AnnualInterestRate,
                request.Purchase.BankFees,
                request.Purchase.LoanInsuranceUpfront,
            }.Any(value => value < 0))
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "FINANCING_INPUT_INVALID", "Income must be positive and scenario amounts cannot be negative.");
        }
        if (!Enum.TryParse<AffordabilityPolicy>(request.Policy, true, out var policy) || policy == AffordabilityPolicy.Custom
            || !Enum.TryParse<PurchaseFundingSource>(request.Purchase.FundingSource, true, out var fundingSource)
            || !Enum.TryParse<PurchaseMethod>(request.Purchase.PurchaseMethod, true, out var purchaseMethod)
            || !Enum.TryParse<LoanRepaymentMethod>(request.Purchase.RepaymentMethod, true, out var repaymentMethod))
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "FINANCING_ENUM_INVALID", "Policy, funding source, purchase method, or repayment method is invalid.");
        }
        if (request.Purchase.SelectedDealerOfferIds.Count > 20
            || request.Purchase.SelectedDealerOfferIds.Any(id => id == Guid.Empty)
            || request.Purchase.DownPaymentPercent > 1
            || request.Purchase.AnnualInterestRate > 1
            || (request.Purchase.DownPaymentAmount is not null && request.Purchase.DownPaymentPercent is not null))
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "FINANCING_INPUT_INVALID", "Offer scope, down payment, or interest rate is invalid.");
        }
        if (fundingSource == PurchaseFundingSource.SelfFunded && request.Purchase.FamilyContribution > 0)
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "FUNDING_SOURCE_MISMATCH", "Use Mixed or FamilyFunded when a family contribution is present.");
        }
        if (purchaseMethod == PurchaseMethod.Loan
            && (request.Purchase.TermMonths is < 1 or > 480
                || (request.Purchase.AnnualInterestRate is null && request.Purchase.InterestRateSourceFactId is null)
                || (request.Purchase.DownPaymentAmount is null && request.Purchase.DownPaymentPercent is null)))
        {
            throw new FinancingCalculationException(StatusCodes.Status400BadRequest, "LOAN_INPUT_INVALID", "A loan needs a 1-480 month term, one down-payment method, and a user or sourced annual interest rate.");
        }
        return new ParsedRequest(policy, fundingSource, purchaseMethod, repaymentMethod);
    }

    private sealed record ParsedRequest(
        AffordabilityPolicy Policy,
        PurchaseFundingSource FundingSource,
        PurchaseMethod PurchaseMethod,
        LoanRepaymentMethod RepaymentMethod);
}
