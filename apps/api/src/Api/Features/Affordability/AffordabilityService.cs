using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietnamCarPlatform.Api.Features.Energy;
using VietnamCarPlatform.Api.Features.Registration;
using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Affordability;

public interface IAffordabilityService
{
    Task<OwnershipCalculationResponse> CalculateOwnershipAsync(OwnershipCalculationRequest request, CancellationToken cancellationToken);
    Task<AffordabilityEvaluationResponse> EvaluateAsync(AffordabilityEvaluationRequest request, CancellationToken cancellationToken);
}

public sealed class AffordabilityService(
    AppDbContext database,
    IEnergyService energyService,
    IRegistrationService registrationService,
    IOptions<AffordabilityOptions> configuredOptions,
    TimeProvider timeProvider) : IAffordabilityService
{
    private readonly AffordabilityOptions options = configuredOptions.Value;

    public async Task<OwnershipCalculationResponse> CalculateOwnershipAsync(
        OwnershipCalculationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOwnership(request);
        var instant = request.CalculationDate ?? timeProvider.GetUtcNow();
        try
        {
            var energy = await energyService.CalculateAsync(ToEnergyRequest(request, instant), cancellationToken);
            var registration = await registrationService.CalculateAsync(new OnRoadCalculationRequest
            {
                TrimId = request.TrimId,
                ProvinceCode = request.ProvinceCode,
                CalculationDate = instant,
                BuyerType = "Individual",
                VehicleType = "PassengerCar",
                FirstInspectionExempt = request.Expenses.FirstInspectionExempt,
                RoadUsageMonths = 12,
            }, cancellationToken);

            var insurance = MonthlyRuleAmount(
                registration,
                "CompulsoryInsurance",
                request.Expenses.CompulsoryInsuranceMonthlyOverride);
            var road = MonthlyRuleAmount(
                registration,
                "RoadUsageFee",
                request.Expenses.RoadUsageMonthlyOverride);
            var inspection = MonthlyRuleAmount(
                registration,
                "InspectionFee",
                request.Expenses.InspectionMonthlyOverride);
            var result = OperatingOwnershipCostEvaluator.Evaluate(new OperatingOwnershipCostInput(
                energy.Result.CurrentCost,
                energy.Result.NormalizedCost,
                request.Expenses.ParkingMonthly,
                request.Expenses.MaintenanceReserveMonthly,
                insurance,
                request.Expenses.BodyInsuranceAnnual / 12,
                road,
                inspection,
                request.Expenses.TyreReserveMonthly,
                request.Expenses.BatteryRentalMonthly,
                options.WorstReasonable.EnergyFactor,
                options.WorstReasonable.ParkingFactor,
                options.WorstReasonable.MaintenanceFactor,
                options.WorstReasonable.TyreFactor));
            result = result with
            {
                Breakdown = result.Breakdown.Select(component => component with
                {
                    Origin = ResolveOrigin(component.Component, request.Expenses),
                }).ToArray(),
            };

            var recurringComponents = new HashSet<string>(StringComparer.Ordinal)
            {
                "CompulsoryInsurance",
                "RoadUsageFee",
                "InspectionFee",
            };
            if (request.Expenses.CompulsoryInsuranceMonthlyOverride is not null)
            {
                recurringComponents.Remove("CompulsoryInsurance");
            }
            if (request.Expenses.RoadUsageMonthlyOverride is not null)
            {
                recurringComponents.Remove("RoadUsageFee");
            }
            if (request.Expenses.InspectionMonthlyOverride is not null)
            {
                recurringComponents.Remove("InspectionFee");
            }

            var warnings = energy.Warnings.Concat(registration.Warnings).Distinct(StringComparer.Ordinal).ToArray();
            return new OwnershipCalculationResponse(
                result,
                new AffordabilityVehicleIdentity(
                    energy.Vehicle.TrimId,
                    energy.Vehicle.BrandName,
                    energy.Vehicle.ModelName,
                    energy.Vehicle.TrimName,
                    energy.Vehicle.ModelYear,
                    energy.Vehicle.Powertrain),
                energy,
                registration.AppliedRules.Where(rule => recurringComponents.Contains(rule.Component)).ToArray(),
                [
                    "OperatingOwnershipCost excludes every loan or financing payment.",
                    "Maintenance and tyre reserves are editable user estimates because no reviewed trim-specific maintenance dataset is published in V1.",
                    $"Worst-reasonable factors: energy {options.WorstReasonable.EnergyFactor:0.##}x, parking {options.WorstReasonable.ParkingFactor:0.##}x, maintenance {options.WorstReasonable.MaintenanceFactor:0.##}x, tyres {options.WorstReasonable.TyreFactor:0.##}x.",
                    "This is an estimate for scenario comparison, not a quote or financial advice.",
                ],
                warnings,
                timeProvider.GetUtcNow());
        }
        catch (EnergyCalculationException exception)
        {
            throw new OwnershipCalculationException(exception.StatusCode, exception.Code, exception.Message);
        }
        catch (RegistrationCalculationException exception)
        {
            throw new OwnershipCalculationException(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    public async Task<AffordabilityEvaluationResponse> EvaluateAsync(
        AffordabilityEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateAffordability(request);
        var policy = Enum.Parse<AffordabilityPolicy>(request.Policy, true);
        var thresholds = options.Thresholds(policy);
        var requestedIds = request.TrimIds.Distinct().ToArray();
        var query = database.CurrentSearchableTrims.AsNoTracking();
        if (requestedIds.Length > 0)
        {
            query = query.Where(car => requestedIds.Contains(car.TrimId));
        }
        var cars = await query
            .OrderBy(car => car.BrandName)
            .ThenBy(car => car.ModelName)
            .ThenBy(car => car.TrimName)
            .Take(200)
            .ToListAsync(cancellationToken);
        var candidateIds = cars.Select(car => car.TrimId).ToArray();
        var profileIds = (await database.EnergyProfiles.AsNoTracking()
            .Where(profile => candidateIds.Contains(profile.TrimId))
            .Select(profile => profile.TrimId)
            .ToArrayAsync(cancellationToken)).ToHashSet();

        var eligible = new List<AffordabilityCandidate>();
        var overBudget = new List<AffordabilityCandidate>();
        var dataExcluded = new List<ExcludedAffordabilityCandidate>();
        foreach (var car in cars)
        {
            var identity = new AffordabilityVehicleIdentity(
                car.TrimId,
                car.BrandName,
                car.ModelName,
                car.TrimName,
                car.ModelYear,
                car.PowertrainType);
            if (!profileIds.Contains(car.TrimId))
            {
                dataExcluded.Add(new ExcludedAffordabilityCandidate(
                    identity,
                    ["ENERGY_PROFILE_UNKNOWN"],
                    "No reviewed official energy profile is available, so the platform will not invent a monthly ownership result."));
                continue;
            }

            try
            {
                var ownership = await CalculateOwnershipAsync(new OwnershipCalculationRequest
                {
                    TrimId = car.TrimId,
                    ProvinceCode = request.ProvinceCode,
                    CalculationDate = request.CalculationDate,
                    Expenses = request.Expenses,
                    Energy = request.Energy,
                }, cancellationToken);
                var evaluation = AffordabilityEvaluator.Evaluate(new AffordabilityEvaluationInput(
                    request.NetMonthlyIncome,
                    request.RentHousing,
                    request.EssentialExpenses,
                    request.OtherFixedDebt,
                    request.SavingsTarget,
                    request.MaximumMonthlyVehicleSpend,
                    thresholds,
                    ownership.Result));
                var candidate = new AffordabilityCandidate(identity, evaluation, ownership);
                (evaluation.Eligible ? eligible : overBudget).Add(candidate);
            }
            catch (OwnershipCalculationException exception)
            {
                dataExcluded.Add(new ExcludedAffordabilityCandidate(
                    identity,
                    [exception.Code],
                    exception.Message));
            }
        }

        var disposable = request.NetMonthlyIncome
            - request.EssentialExpenses
            - request.RentHousing
            - request.OtherFixedDebt
            - request.SavingsTarget;
        return new AffordabilityEvaluationResponse(
            policy.ToString(),
            thresholds,
            new AffordabilityProfileSummary(
                request.NetMonthlyIncome,
                request.RentHousing,
                request.EssentialExpenses,
                request.OtherFixedDebt,
                request.SavingsTarget,
                request.MaximumMonthlyVehicleSpend,
                disposable,
                "VND"),
            eligible.OrderBy(value => value.Ownership.Result.NormalizedMonthlyCost).ToArray(),
            overBudget.OrderBy(value => value.Ownership.Result.NormalizedMonthlyCost).ToArray(),
            dataExcluded,
            [
                "Eligibility uses normalized operating ownership cost; a temporary promotion cannot make a structurally unaffordable car pass.",
                "Purchase price, upfront cash and financing payments are intentionally outside V1.7 ownership eligibility and are evaluated separately in V1.8.",
                "Policy thresholds are configurable product guardrails, not financial advice.",
            ],
            timeProvider.GetUtcNow());
    }

    private static EnergyCalculationRequest ToEnergyRequest(OwnershipCalculationRequest request, DateTimeOffset instant) => new()
    {
        TrimId = request.TrimId,
        CalculationDate = instant,
        MonthlyKilometres = request.Expenses.MonthlyKilometres,
        FuelType = request.Energy.FuelType,
        EvShare = request.Energy.EvShare,
        HomeChargingShare = request.Energy.HomeChargingShare,
        ChargingEfficiency = request.Energy.ChargingEfficiency,
        HomeMode = request.Energy.HomeMode,
        HouseholdBaseKwh = request.Energy.HouseholdBaseKwh,
        CustomHomeAmountPerKwh = request.Energy.CustomHomeAmountPerKwh,
        ChargingProviderSlug = request.Energy.ChargingProviderSlug,
        ConnectorType = request.Energy.ConnectorType,
        ChargingPowerKw = request.Energy.ChargingPowerKw,
        PublicSessions = request.Energy.PublicSessions,
        SessionsUsedThisMonth = request.Energy.SessionsUsedThisMonth,
        PostChargeMinutesPerSession = request.Energy.PostChargeMinutesPerSession,
        CustomerType = request.Energy.CustomerType,
        PurchaseDate = request.Energy.PurchaseDate,
        PromotionEligibilityConfirmed = request.Energy.PromotionEligibilityConfirmed,
    };

    private static decimal MonthlyRuleAmount(
        OnRoadCalculationResponse response,
        string component,
        decimal? monthlyOverride)
    {
        if (monthlyOverride is not null)
        {
            return monthlyOverride.Value;
        }
        var amount = response.Breakdown.SingleOrDefault(value => value.Component == component)?.Amount ?? 0;
        return amount / 12;
    }

    private static string ResolveOrigin(string component, OwnershipExpenseAssumptionsRequest expenses) => component switch
    {
        "CompulsoryInsurance" => expenses.CompulsoryInsuranceMonthlyOverride is null ? "SourcedLegalRule" : "UserOverride",
        "RoadUsage" => expenses.RoadUsageMonthlyOverride is null ? "SourcedLegalRule" : "UserOverride",
        "Inspection" => expenses.InspectionMonthlyOverride is null ? "SourcedLegalRule" : "UserOverride",
        _ => component is "Energy" ? "SourcedCalculation" : component is "MaintenanceReserve" or "TyreReserve" ? "UserEstimate" : "UserAssumption",
    };

    private static void ValidateOwnership(OwnershipCalculationRequest request)
    {
        if (request.TrimId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProvinceCode))
        {
            throw new OwnershipCalculationException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "TrimId and ProvinceCode are required.");
        }
        ValidateExpenseAmounts(request.Expenses);
    }

    private static void ValidateExpenseAmounts(OwnershipExpenseAssumptionsRequest expenses)
    {
        var amounts = new decimal?[]
        {
            expenses.MonthlyKilometres,
            expenses.ParkingMonthly,
            expenses.MaintenanceReserveMonthly,
            expenses.BodyInsuranceAnnual,
            expenses.TyreReserveMonthly,
            expenses.BatteryRentalMonthly,
            expenses.CompulsoryInsuranceMonthlyOverride,
            expenses.RoadUsageMonthlyOverride,
            expenses.InspectionMonthlyOverride,
        };
        if (amounts.Any(value => value < 0))
        {
            throw new OwnershipCalculationException(StatusCodes.Status400BadRequest, "OWNERSHIP_INPUT_INVALID", "Ownership expenses and reserves cannot be negative.");
        }
    }

    private static void ValidateAffordability(AffordabilityEvaluationRequest request)
    {
        if (request.NetMonthlyIncome <= 0
            || string.IsNullOrWhiteSpace(request.ProvinceCode)
            || request.RentHousing < 0
            || request.EssentialExpenses < 0
            || request.OtherFixedDebt < 0
            || request.SavingsTarget < 0
            || request.MaximumMonthlyVehicleSpend < 0)
        {
            throw new OwnershipCalculationException(StatusCodes.Status400BadRequest, "AFFORDABILITY_INPUT_INVALID", "Income must be positive and household cash-flow inputs cannot be negative.");
        }
        if (request.TrimIds.Count > 200 || request.TrimIds.Any(id => id == Guid.Empty))
        {
            throw new OwnershipCalculationException(StatusCodes.Status400BadRequest, "AFFORDABILITY_TRIM_SCOPE_INVALID", "TrimIds can contain at most 200 non-empty trim identifiers.");
        }
        if (!Enum.TryParse<AffordabilityPolicy>(request.Policy, true, out var policy)
            || policy == AffordabilityPolicy.Custom)
        {
            throw new OwnershipCalculationException(StatusCodes.Status400BadRequest, "AFFORDABILITY_POLICY_INVALID", "Policy must be Conservative, Balanced, or Aggressive.");
        }
        ValidateExpenseAmounts(request.Expenses);
    }
}
