using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Api.Features.Affordability;
using VietnamCarPlatform.Api.Features.Energy;
using VietnamCarPlatform.Api.Features.Financing;
using VietnamCarPlatform.Api.Features.Registration;
using VietnamCarPlatform.Domain.Affordability;
using VietnamCarPlatform.Domain.Catalog;
using VietnamCarPlatform.Domain.Commerce;
using VietnamCarPlatform.Domain.Compare;
using VietnamCarPlatform.Domain.Common;
using VietnamCarPlatform.Infrastructure.Catalog;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Compare;

public interface ICompareService
{
    Task<CompareCalculationResponse> CalculateAsync(CompareCalculationRequest request, CancellationToken cancellationToken);
}

public sealed class CompareService(
    AppDbContext database,
    IFinancingService financingService,
    IRegistrationService registrationService,
    TimeProvider timeProvider) : ICompareService
{
    public async Task<CompareCalculationResponse> CalculateAsync(
        CompareCalculationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var trimIds = request.TrimIds.Distinct().ToArray();
        var instant = (request.CalculationDate ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var cars = await database.CurrentSearchableTrims.AsNoTracking()
            .Where(car => trimIds.Contains(car.TrimId))
            .ToListAsync(cancellationToken);
        if (cars.Count != trimIds.Length)
        {
            throw new CompareCalculationException(
                StatusCodes.Status404NotFound,
                "COMPARE_TRIM_NOT_FOUND",
                "Every compared identifier must resolve to a currently published Vietnam-market trim.");
        }
        cars = trimIds.Select(id => cars.Single(car => car.TrimId == id)).ToList();

        var priceRows = await database.Prices.AsNoTracking()
            .Where(price => trimIds.Contains(price.TrimId)
                && price.EffectiveFrom <= instant
                && (price.EffectiveTo == null || price.EffectiveTo > instant)
                && (price.RegionScope == request.ProvinceCode || price.RegionScope == "VN"))
            .ToListAsync(cancellationToken);
        var specificationRaw = await (
                from value in database.TrimSpecs.AsNoTracking()
                join definition in database.SpecDefinitions.AsNoTracking() on value.SpecDefinitionId equals definition.Id
                where trimIds.Contains(value.TrimId)
                select new { Value = value, Definition = definition })
            .ToListAsync(cancellationToken);
        var specificationRows = specificationRaw
            .Select(row => new SpecificationComparisonRow(row.Value, row.Definition))
            .ToArray();
        var featureRaw = await (
                from value in database.TrimFeatures.AsNoTracking()
                join definition in database.FeatureDefinitions.AsNoTracking() on value.FeatureDefinitionId equals definition.Id
                where trimIds.Contains(value.TrimId)
                select new { Value = value, Definition = definition })
            .ToListAsync(cancellationToken);
        var featureRows = featureRaw
            .Select(row => new FeatureComparisonRow(row.Value, row.Definition))
            .ToArray();
        var sourceFactIds = priceRows.Select(row => row.SourceFactId)
            .Concat(specificationRows.Select(row => row.Value.SourceFactId))
            .Concat(featureRows.Select(row => row.Value.SourceFactId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var sourceMap = await LoadSourcesAsync(sourceFactIds, cancellationToken);

        var calculations = new Dictionary<Guid, CalculationSnapshot>();
        foreach (var car in cars)
        {
            calculations[car.TrimId] = await CalculateVehicleAsync(request, car, instant, cancellationToken);
        }

        var rows = new List<CompareRow>();
        AddIdentityRows(rows, cars);
        AddPriceRows(rows, cars, priceRows, sourceMap, request.ProvinceCode);
        AddCalculationRows(rows, cars, calculations);
        AddSpecificationRows(rows, cars, specificationRows, sourceMap);
        AddFeatureRows(rows, cars, featureRows, sourceMap);
        var warnings = calculations.Values
            .SelectMany(value => value.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new CompareCalculationResponse(
            cars.Select(car => new CompareVehicleHeader(
                car.TrimId,
                car.BrandName,
                car.ModelName,
                car.TrimName,
                car.ModelYear,
                car.BodyType,
                car.Segment,
                car.PowertrainType,
                car.DataUpdatedAt)).ToArray(),
            new CompareScenarioSummary(
                request.ProvinceCode,
                instant,
                request.ProfilePreset,
                request.FinancingPreset,
                request.Policy,
                request.Expenses.MonthlyKilometres,
                request.Expenses.ParkingMonthly,
                request.Purchase.FundingSource,
                request.Purchase.PurchaseMethod,
                request.Purchase.RepaymentMethod,
                request.Purchase.AnnualInterestRate,
                request.Purchase.TermMonths,
                request.Purchase.DownPaymentPercent,
                "VND"),
            rows,
            warnings,
            timeProvider.GetUtcNow());
    }

    private async Task<CalculationSnapshot> CalculateVehicleAsync(
        CompareCalculationRequest request,
        CurrentSearchableTrim car,
        DateTimeOffset instant,
        CancellationToken cancellationToken)
    {
        try
        {
            var full = await financingService.CalculateAsync(ToFinancingRequest(request, car.TrimId, instant), cancellationToken);
            return new CalculationSnapshot(full, full.OnRoad, full.Financing, null, null, full.Warnings);
        }
        catch (FinancingCalculationException exception)
        {
            try
            {
                var onRoad = await registrationService.CalculateAsync(new OnRoadCalculationRequest
                {
                    TrimId = car.TrimId,
                    ProvinceCode = request.ProvinceCode,
                    CalculationDate = instant,
                    BuyerType = "Individual",
                    VehicleType = "PassengerCar",
                    FirstInspectionExempt = request.Expenses.FirstInspectionExempt,
                    RoadUsageMonths = 12,
                }, cancellationToken);
                var fallback = request.Purchase.InterestRateSourceFactId is null
                    ? CalculateFinancingFallback(request.Purchase, onRoad.Result.OnRoadPrice)
                    : null;
                return new CalculationSnapshot(
                    null,
                    onRoad,
                    fallback,
                    exception.Code,
                    exception.Message,
                    onRoad.Warnings.Append($"{exception.Code}: {exception.Message}").ToArray());
            }
            catch (RegistrationCalculationException registrationException)
            {
                return new CalculationSnapshot(
                    null,
                    null,
                    null,
                    registrationException.Code,
                    registrationException.Message,
                    [$"{exception.Code}: {exception.Message}", $"{registrationException.Code}: {registrationException.Message}"]);
            }
        }
    }

    private static FinancingCalculationResult? CalculateFinancingFallback(PurchaseFundingRequest purchase, decimal acquisitionCost)
    {
        if (!Enum.TryParse<PurchaseFundingSource>(purchase.FundingSource, true, out var funding)
            || !Enum.TryParse<PurchaseMethod>(purchase.PurchaseMethod, true, out var method)
            || !Enum.TryParse<LoanRepaymentMethod>(purchase.RepaymentMethod, true, out var repayment))
        {
            return null;
        }
        try
        {
            return FinancingCalculator.Calculate(new FinancingCalculationInput(
                acquisitionCost,
                purchase.AvailableCash,
                purchase.FamilyContribution,
                purchase.TradeInNetValue,
                0,
                method == PurchaseMethod.Loan ? purchase.DownPaymentAmount : null,
                method == PurchaseMethod.Loan ? purchase.DownPaymentPercent : null,
                method == PurchaseMethod.Loan ? purchase.AnnualInterestRate ?? 0 : 0,
                method == PurchaseMethod.Loan ? purchase.TermMonths : 0,
                method == PurchaseMethod.Loan ? purchase.BankFees + purchase.LoanInsuranceUpfront : 0,
                funding,
                method,
                repayment));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static FinancingCalculationRequest ToFinancingRequest(
        CompareCalculationRequest request,
        Guid trimId,
        DateTimeOffset instant) => new()
        {
            TrimId = trimId,
            ProvinceCode = request.ProvinceCode,
            CalculationDate = instant,
            Policy = request.Policy,
            NetMonthlyIncome = request.NetMonthlyIncome,
            RentHousing = request.RentHousing,
            EssentialExpenses = request.EssentialExpenses,
            OtherFixedDebt = request.OtherFixedDebt,
            SavingsTarget = request.SavingsTarget,
            MaximumMonthlyVehicleSpend = request.MaximumMonthlyVehicleSpend,
            Expenses = request.Expenses,
            Energy = request.Energy,
            Purchase = request.Purchase,
        };

    private static void AddIdentityRows(List<CompareRow> rows, IReadOnlyList<CurrentSearchableTrim> cars)
    {
        rows.Add(Row("body_type", "Kiểu thân xe", "Tổng quan", null, "Text", cars.Select(car => Cell(car.TrimId, State(car.BodyType), text: Value(car.BodyType))).ToArray()));
        rows.Add(Row("segment", "Phân khúc", "Tổng quan", null, "Text", cars.Select(car => Cell(car.TrimId, State(car.Segment), text: Value(car.Segment))).ToArray()));
        rows.Add(Row("powertrain", "Hệ truyền động", "Tổng quan", null, "Text", cars.Select(car => Cell(car.TrimId, State(car.PowertrainType), text: Value(car.PowertrainType))).ToArray()));
    }

    private static void AddPriceRows(
        List<CompareRow> rows,
        IReadOnlyList<CurrentSearchableTrim> cars,
        IReadOnlyList<Price> prices,
        IReadOnlyDictionary<Guid, CompareSourceReference> sources,
        string provinceCode)
    {
        rows.Add(PriceRow("msrp", "Giá niêm yết (MSRP)", PriceType.Msrp));
        rows.Add(PriceRow("promotion_price", "Giá khuyến mại", PriceType.PromotionPrice));
        rows.Add(PriceRow("dealer_cash_price", "Giá tiền mặt đại lý", PriceType.DealerCashPrice));
        rows.Add(CurrentCashRow());
        return;

        CompareRow PriceRow(string code, string label, PriceType type) => Row(
            code,
            label,
            "Giá mua",
            "VND",
            "Money",
            cars.Select(car => PriceCell(car.TrimId, SelectPrice(car.TrimId, [type]))).ToArray());

        CompareRow CurrentCashRow() => Row(
            "current_cash_price",
            "Giá tiền mặt hiện hành",
            "Giá mua",
            "VND",
            "Money",
            cars.Select(car => PriceCell(car.TrimId, SelectPrice(car.TrimId, [PriceType.DealerCashPrice, PriceType.PromotionPrice, PriceType.Msrp]))).ToArray());

        Price? SelectPrice(Guid trimId, IReadOnlyList<PriceType> types)
        {
            foreach (var type in types)
            {
                var selected = prices
                    .Where(price => price.TrimId == trimId
                        && price.PriceType == type
                        && price.Amount is not null
                        && price.Status is PriceStatus.Official or PriceStatus.Expected)
                    .OrderByDescending(price => price.RegionScope == provinceCode)
                    .ThenBy(price => price.Status == PriceStatus.Official ? 0 : 1)
                    .ThenBy(price => price.Priority)
                    .ThenByDescending(price => price.Version)
                    .FirstOrDefault();
                if (selected is not null)
                {
                    return selected;
                }
            }
            return null;
        }

        CompareCell PriceCell(Guid trimId, Price? price) => price is null
            ? Cell(trimId, "Unknown", note: "Chưa có giá hiệu lực được xác minh cho loại này.")
            : Cell(
                trimId,
                price.Status.ToString(),
                numeric: price.Amount,
                source: SourceOne(price.SourceFactId, sources),
                note: price.PriceType.ToString());
    }

    private static void AddCalculationRows(
        List<CompareRow> rows,
        IReadOnlyList<CurrentSearchableTrim> cars,
        IReadOnlyDictionary<Guid, CalculationSnapshot> calculations)
    {
        rows.Add(CalculatedRow("on_road", "Giá ra biển", "Chi phí tính toán", "VND", "Money", value => value.OnRoad?.Result.OnRoadPrice, value => OnRoadSources(value.OnRoad)));
        rows.Add(CalculatedRow("upfront_cash", "Tiền mặt cần ban đầu", "Chi phí tính toán", "VND", "Money", value => value.Financing?.UpfrontCashRequired));
        rows.Add(CalculatedRow("installment", "Khoản trả vay dùng cho gate", "Chi phí tính toán", "VND/tháng", "Money", value => value.Financing?.MonthlyPaymentForCommitment, state: value => value.Financing?.FinancingStatus == "NotApplicable" ? "NotApplicable" : "Calculated"));
        rows.Add(CalculatedRow("ownership_current", "Chi phí sở hữu hiện tại", "Chi phí tính toán", "VND/tháng", "Money", value => value.Full?.Ownership.Result.CurrentMonthlyCost, value => OwnershipSources(value.Full)));
        rows.Add(CalculatedRow("ownership_normalized", "Chi phí sở hữu chuẩn hóa", "Chi phí tính toán", "VND/tháng", "Money", value => value.Full?.Ownership.Result.NormalizedMonthlyCost, value => OwnershipSources(value.Full)));
        rows.Add(CalculatedRow("total_monthly_commitment", "Tổng cam kết xe mỗi tháng", "Chi phí tính toán", "VND/tháng", "Money", value => value.Full?.PurchaseCashflow.TotalMonthlyVehicleCommitment));
        rows.Add(Row(
            "purchase_rating",
            "Đánh giá mua/vay",
            "Chi phí tính toán",
            null,
            "Text",
            cars.Select(car =>
            {
                var value = calculations[car.TrimId];
                return value.Full is null
                    ? Cell(car.TrimId, "Unknown", note: Error(value))
                    : Cell(car.TrimId, "Calculated", text: value.Full.PurchaseRating);
            }).ToArray()));
        return;

        CompareRow CalculatedRow(
            string code,
            string label,
            string section,
            string unit,
            string format,
            Func<CalculationSnapshot, decimal?> selector,
            Func<CalculationSnapshot, IReadOnlyList<CompareSourceReference>>? sourceSelector = null,
            Func<CalculationSnapshot, string>? state = null) => Row(
                code,
                label,
                section,
                unit,
                format,
                cars.Select(car =>
                {
                    var snapshot = calculations[car.TrimId];
                    var numeric = selector(snapshot);
                    return numeric is null
                        ? Cell(car.TrimId, "Unknown", note: Error(snapshot))
                        : Cell(
                            car.TrimId,
                            state?.Invoke(snapshot) ?? "Calculated",
                            numeric: numeric,
                            sources: sourceSelector?.Invoke(snapshot),
                            note: snapshot.ErrorCode is null ? null : $"Partial: {snapshot.ErrorCode}");
                }).ToArray());
    }

    private static void AddSpecificationRows(
        List<CompareRow> rows,
        IReadOnlyList<CurrentSearchableTrim> cars,
        IReadOnlyList<SpecificationComparisonRow> values,
        IReadOnlyDictionary<Guid, CompareSourceReference> sources)
    {
        var definitions = values
            .GroupBy(value => value.Definition.Id)
            .Select(group => group.First().Definition)
            .OrderBy(value => value.Group)
            .ThenBy(value => value.Label)
            .ToArray();
        foreach (var definition in definitions)
        {
            var cells = cars.Select(car =>
            {
                var row = values.FirstOrDefault(value => value.Value.TrimId == car.TrimId && value.Definition.Id == definition.Id);
                if (row is null)
                {
                    return Cell(car.TrimId, "Unknown");
                }
                return Cell(
                    car.TrimId,
                    row.Value.Status.ToString(),
                    numeric: row.Value.NumericValue,
                    text: row.Value.TextValue ?? row.Value.EnumValue,
                    source: SourceOne(row.Value.SourceFactId, sources));
            }).ToArray();
            rows.Add(Row(
                definition.Code,
                definition.Label,
                $"Thông số · {definition.Group}",
                definition.CanonicalUnit,
                "Number",
                cells));
        }
    }

    private static void AddFeatureRows(
        List<CompareRow> rows,
        IReadOnlyList<CurrentSearchableTrim> cars,
        IReadOnlyList<FeatureComparisonRow> values,
        IReadOnlyDictionary<Guid, CompareSourceReference> sources)
    {
        var definitions = values
            .GroupBy(value => value.Definition.Id)
            .Select(group => group.First().Definition)
            .OrderBy(value => value.Group)
            .ThenBy(value => value.Label)
            .ToArray();
        foreach (var definition in definitions)
        {
            var cells = cars.Select(car =>
            {
                var row = values.FirstOrDefault(value => value.Value.TrimId == car.TrimId && value.Definition.Id == definition.Id);
                if (row is null)
                {
                    return Cell(car.TrimId, "Unknown");
                }
                return Cell(
                    car.TrimId,
                    row.Value.Status.ToString(),
                    numeric: row.Value.NumericValue,
                    text: row.Value.TextValue ?? row.Value.EnumValue,
                    boolean: row.Value.BooleanValue,
                    source: SourceOne(row.Value.SourceFactId, sources));
            }).ToArray();
            rows.Add(Row(
                definition.Code,
                definition.Label,
                $"Trang bị · {definition.Group}",
                null,
                "Boolean",
                cells));
        }
    }

    private async Task<Dictionary<Guid, CompareSourceReference>> LoadSourcesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        var rows = await (
                from fact in database.SourceFacts.AsNoTracking()
                join snapshot in database.SourceSnapshots.AsNoTracking() on fact.SnapshotId equals snapshot.Id
                join source in database.Sources.AsNoTracking() on snapshot.SourceId equals source.Id
                where ids.Contains(fact.Id)
                select new { Fact = fact, Snapshot = snapshot, Source = source })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Fact.Id, row => new CompareSourceReference(
            row.Fact.Id,
            row.Source.Id,
            row.Source.Name,
            row.Source.Url,
            row.Source.AuthorityLevel.ToString(),
            row.Source.ContentType.ToString(),
            row.Snapshot.FetchedAt,
            row.Snapshot.ContentHash,
            row.Fact.Status.ToString(),
            row.Fact.Confidence.ToString()));
    }

    private static CompareRow Row(
        string code,
        string label,
        string section,
        string? unit,
        string format,
        IReadOnlyList<CompareCell> cells) => new(
            code,
            label,
            section,
            unit,
            format,
            ComparisonDifferenceEvaluator.HasDifference(cells.Select(cell => new ComparisonValue(
                cell.State,
                cell.NumericValue,
                cell.TextValue,
                cell.BooleanValue))),
            cells);

    private static CompareCell Cell(
        Guid trimId,
        string state,
        decimal? numeric = null,
        string? text = null,
        bool? boolean = null,
        CompareSourceReference? source = null,
        IReadOnlyList<CompareSourceReference>? sources = null,
        string? note = null) => new(
            trimId,
            state,
            numeric,
            text,
            boolean,
            sources ?? (source is null ? [] : [source]),
            note);

    private static CompareSourceReference? SourceOne(
        Guid? sourceFactId,
        IReadOnlyDictionary<Guid, CompareSourceReference> sources) =>
        sourceFactId is Guid id ? sources.GetValueOrDefault(id) : null;

    private static CompareSourceReference[] OnRoadSources(OnRoadCalculationResponse? response)
    {
        if (response is null)
        {
            return [];
        }
        return response.AppliedRules.Select(rule => rule.Source)
            .Append(response.InputPrice.Source)
            .Where(source => source is not null)
            .Select(source => FromRuleSource(source!))
            .DistinctBy(source => source.SourceFactId)
            .ToArray();
    }

    private static CompareSourceReference[] OwnershipSources(FinancingCalculationResponse? response)
    {
        if (response is null)
        {
            return [];
        }
        var rules = response.Ownership.AppliedRecurringRules
            .Where(rule => rule.Source is not null)
            .Select(rule => FromRuleSource(rule.Source!));
        var energy = response.Ownership.Energy.AppliedRates
            .Where(rate => rate.Source is not null)
            .Select(rate => FromEnergySource(rate.Source!))
            .Append(response.Ownership.Energy.EnergyProfile.Source is null
                ? null
                : FromEnergySource(response.Ownership.Energy.EnergyProfile.Source))
            .Where(source => source is not null)
            .Select(source => source!);
        return rules.Concat(energy).DistinctBy(source => source.SourceFactId).ToArray();
    }

    private static CompareSourceReference FromRuleSource(RuleSourceReference source) => new(
        source.SourceFactId,
        source.SourceId,
        source.Name,
        source.Url,
        source.Authority,
        source.ContentType,
        source.FetchedAt,
        source.ContentHash,
        source.FactStatus,
        source.Confidence);

    private static CompareSourceReference FromEnergySource(EnergySourceReference source) => new(
        source.SourceFactId,
        source.SourceId,
        source.Name,
        source.Url,
        source.Authority,
        source.ContentType,
        source.FetchedAt,
        source.ContentHash,
        source.FactStatus,
        source.Confidence);

    private static string State(string value) => string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown" : "Official";
    private static string? Value(string value) => string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) ? null : value;
    private static string Error(CalculationSnapshot value) => value.ErrorCode is null ? "Chưa tính được." : $"{value.ErrorCode}: {value.ErrorMessage}";

    private static void Validate(CompareCalculationRequest request)
    {
        var unique = request.TrimIds.Distinct().ToArray();
        if (request.TrimIds.Count is < 2 or > 4
            || unique.Length != request.TrimIds.Count
            || unique.Any(id => id == Guid.Empty)
            || string.IsNullOrWhiteSpace(request.ProvinceCode)
            || request.NetMonthlyIncome <= 0)
        {
            throw new CompareCalculationException(StatusCodes.Status400BadRequest, "COMPARE_INPUT_INVALID", "Compare requires 2-4 unique trims, an active province and a positive scenario income.");
        }
        if (request.Purchase.SelectedDealerOfferIds.Count > 0)
        {
            throw new CompareCalculationException(StatusCodes.Status400BadRequest, "COMPARE_OFFER_SCOPE_INVALID", "A dealer offer belongs to one trim and cannot be applied as a shared compare scenario; dealer cash prices remain visible as separate sourced rows.");
        }
    }

    private sealed record CalculationSnapshot(
        FinancingCalculationResponse? Full,
        OnRoadCalculationResponse? OnRoad,
        FinancingCalculationResult? Financing,
        string? ErrorCode,
        string? ErrorMessage,
        IReadOnlyList<string> Warnings);

    private sealed record SpecificationComparisonRow(TrimSpec Value, SpecDefinition Definition);
    private sealed record FeatureComparisonRow(TrimFeature Value, FeatureDefinition Definition);
}
