using System.Globalization;
using System.Text.Json;

namespace VietnamCarPlatform.Domain.Rules;

public sealed record RegistrationRuleContext(
    string ProvinceCode,
    string AreaClass,
    string VehicleType,
    decimal? Seats,
    string Powertrain,
    string BuyerType,
    bool FirstInspectionExempt,
    int RoadUsageMonths,
    decimal EffectiveCashPurchasePrice,
    IReadOnlyDictionary<string, object?>? Attributes = null)
{
    public object? Value(string field) => field switch
    {
        "provinceCode" => ProvinceCode,
        "areaClass" => AreaClass,
        "vehicleType" => VehicleType,
        "seats" => Seats,
        "powertrain" => Powertrain,
        "buyerType" => BuyerType,
        "firstInspectionExempt" => FirstInspectionExempt,
        "roadUsageMonths" => RoadUsageMonths,
        "effectiveCashPurchasePrice" => EffectiveCashPurchasePrice,
        _ => Attributes is not null && Attributes.TryGetValue(field, out var value) ? value : null,
    };
}

public sealed record EvaluatedRegistrationRule(RegistrationRule Rule, decimal Amount);

public static class RegistrationRuleEvaluator
{
    public static IReadOnlyList<EvaluatedRegistrationRule> Evaluate(
        IEnumerable<RegistrationRule> rules,
        RegistrationRuleContext context,
        DateTimeOffset instant)
    {
        return rules
            .Where(rule => rule.IsEffectiveAt(instant) && Matches(rule.ScopeJson, context))
            .GroupBy(rule => rule.Component)
            .Select(group => group.OrderBy(rule => rule.Priority).ThenByDescending(rule => rule.Version).First())
            .Select(rule => new EvaluatedRegistrationRule(rule, Calculate(rule, context)))
            .OrderBy(result => result.Rule.Component)
            .ToArray();
    }

    public static bool Matches(string scopeJson, RegistrationRuleContext context)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(scopeJson) ? "{}" : scopeJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Registration rule scope must be a JSON object.");
        }

        if (root.TryGetProperty("condition", out var condition))
        {
            return EvaluateCondition(condition, context);
        }

        if (root.TryGetProperty("all", out _) || root.TryGetProperty("any", out _) || root.TryGetProperty("not", out _))
        {
            return EvaluateCondition(root, context);
        }

        foreach (var property in root.EnumerateObject())
        {
            var actual = context.Value(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (!property.Value.EnumerateArray().Any(expected => Equal(actual, expected)))
                {
                    return false;
                }
            }
            else if (!Equal(actual, property.Value))
            {
                return false;
            }
        }

        return true;
    }

    public static decimal Calculate(RegistrationRule rule, RegistrationRuleContext context)
    {
        using var document = JsonDocument.Parse(rule.ParametersJson);
        var parameters = document.RootElement;
        var amount = rule.CalculationType switch
        {
            CalculationType.Fixed => RequiredDecimal(parameters, "amount"),
            CalculationType.Percentage => Basis(parameters, context) * RequiredDecimal(parameters, "rate"),
            CalculationType.Tiered => CalculateTiered(parameters, context),
            CalculationType.Formula => CalculateFormula(parameters, context),
            _ => throw new InvalidOperationException($"Unsupported calculation type: {rule.CalculationType}."),
        };

        return decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
    }

    private static bool EvaluateCondition(JsonElement node, RegistrationRuleContext context)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Condition node must be a JSON object.");
        }

        if (node.TryGetProperty("all", out var all))
        {
            return all.EnumerateArray().All(child => EvaluateCondition(child, context));
        }

        if (node.TryGetProperty("any", out var any))
        {
            return any.EnumerateArray().Any(child => EvaluateCondition(child, context));
        }

        if (node.TryGetProperty("not", out var not))
        {
            return !EvaluateCondition(not, context);
        }

        var field = node.GetProperty("field").GetString()
            ?? throw new InvalidOperationException("Condition field is required.");
        var operation = node.GetProperty("operator").GetString()?.ToLowerInvariant()
            ?? throw new InvalidOperationException("Condition operator is required.");
        var actual = context.Value(field);
        return operation switch
        {
            "equals" => Equal(actual, node.GetProperty("value")),
            "notequals" => !Equal(actual, node.GetProperty("value")),
            "in" => node.GetProperty("values").EnumerateArray().Any(value => Equal(actual, value)),
            "notin" => node.GetProperty("values").EnumerateArray().All(value => !Equal(actual, value)),
            "lt" => TryCompare(actual, node.GetProperty("value"), comparison => comparison < 0),
            "lte" => TryCompare(actual, node.GetProperty("value"), comparison => comparison <= 0),
            "gt" => TryCompare(actual, node.GetProperty("value"), comparison => comparison > 0),
            "gte" => TryCompare(actual, node.GetProperty("value"), comparison => comparison >= 0),
            "exists" => actual is not null == node.GetProperty("value").GetBoolean(),
            _ => throw new InvalidOperationException($"Unsupported condition operator: {operation}."),
        };
    }

    private static decimal CalculateTiered(JsonElement parameters, RegistrationRuleContext context)
    {
        var basis = Basis(parameters, context);
        var progressive = parameters.TryGetProperty("mode", out var mode)
            && string.Equals(mode.GetString(), "progressive", StringComparison.OrdinalIgnoreCase);
        decimal total = 0;
        foreach (var tier in parameters.GetProperty("tiers").EnumerateArray())
        {
            var from = tier.TryGetProperty("from", out var fromElement) ? fromElement.GetDecimal() : 0;
            var to = tier.TryGetProperty("to", out var toElement) && toElement.ValueKind != JsonValueKind.Null
                ? toElement.GetDecimal()
                : (decimal?)null;
            if (basis < from || (to is not null && basis > to.Value && !progressive))
            {
                continue;
            }

            var tierBasis = progressive ? Math.Max(0, Math.Min(basis, to ?? basis) - from) : basis;
            var value = tier.TryGetProperty("amount", out var fixedAmount)
                ? fixedAmount.GetDecimal()
                : tierBasis * tier.GetProperty("rate").GetDecimal();
            if (!progressive)
            {
                return value;
            }

            total += value;
        }

        return total;
    }

    private static decimal CalculateFormula(JsonElement parameters, RegistrationRuleContext context)
    {
        var name = parameters.GetProperty("name").GetString();
        return name switch
        {
            "addVat" => RequiredDecimal(parameters, "baseAmount") * (1 + RequiredDecimal(parameters, "vatRate")),
            "monthly" => RequiredDecimal(parameters, "monthlyAmount") * context.RoadUsageMonths,
            _ => throw new InvalidOperationException($"Unsupported registration formula: {name}."),
        };
    }

    private static decimal Basis(JsonElement parameters, RegistrationRuleContext context)
    {
        var field = parameters.TryGetProperty("basis", out var basis)
            ? basis.GetString() ?? "effectiveCashPurchasePrice"
            : "effectiveCashPurchasePrice";
        return ToDecimal(context.Value(field));
    }

    private static decimal RequiredDecimal(JsonElement element, string name) => element.GetProperty(name).GetDecimal();

    private static bool Equal(object? actual, JsonElement expected)
    {
        if (actual is null)
        {
            return expected.ValueKind == JsonValueKind.Null;
        }

        return expected.ValueKind switch
        {
            JsonValueKind.String => string.Equals(Convert.ToString(actual, CultureInfo.InvariantCulture), expected.GetString(), StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => ToDecimal(actual) == expected.GetDecimal(),
            JsonValueKind.True => Convert.ToBoolean(actual, CultureInfo.InvariantCulture),
            JsonValueKind.False => !Convert.ToBoolean(actual, CultureInfo.InvariantCulture),
            _ => false,
        };
    }

    private static bool TryCompare(object? actual, JsonElement expected, Func<int, bool> predicate) =>
        actual is not null && predicate(ToDecimal(actual).CompareTo(expected.GetDecimal()));

    private static decimal ToDecimal(object? value) => value switch
    {
        null => throw new InvalidOperationException("A numeric condition or basis used an unknown value."),
        decimal number => number,
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
    };
}
