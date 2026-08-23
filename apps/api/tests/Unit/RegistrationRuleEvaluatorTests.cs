using VietnamCarPlatform.Domain.Rules;

namespace VietnamCarPlatform.Api.UnitTests;

public sealed class RegistrationRuleEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 0, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void SelectsEffectiveHighestPriorityRuleAndEvaluatesNestedConditions()
    {
        var rules = new[]
        {
            Rule(RegistrationComponent.PlateAndRegistrationFee, CalculationType.Fixed, "{\"amount\":14000000}", 1,
                "{\"all\":[{\"field\":\"areaClass\",\"operator\":\"equals\",\"value\":\"I\"},{\"any\":[{\"field\":\"seats\",\"operator\":\"lte\",\"value\":9},{\"field\":\"vehicleType\",\"operator\":\"equals\",\"value\":\"Motorcycle\"}]}]}", version: 2),
            Rule(RegistrationComponent.PlateAndRegistrationFee, CalculationType.Fixed, "{\"amount\":999}", 2, "{}"),
        };

        var result = Assert.Single(RegistrationRuleEvaluator.Evaluate(rules, Context(), Now));
        Assert.Equal(14_000_000m, result.Amount);
        Assert.Equal(2, result.Rule.Version);
    }

    [Fact]
    public void SupportsPercentageTieredAndFormulaWithoutExecutingArbitraryExpressions()
    {
        var percentage = Rule(RegistrationComponent.FirstRegistrationTax, CalculationType.Percentage, "{\"rate\":0.1,\"basis\":\"effectiveCashPurchasePrice\"}");
        var tiered = Rule(RegistrationComponent.Other, CalculationType.Tiered, "{\"basis\":\"seats\",\"tiers\":[{\"from\":0,\"to\":5,\"amount\":100},{\"from\":6,\"to\":9,\"amount\":200}]}");
        var formula = Rule(RegistrationComponent.CompulsoryInsurance, CalculationType.Formula, "{\"name\":\"addVat\",\"baseAmount\":437000,\"vatRate\":0.1}");

        Assert.Equal(64_600_000m, RegistrationRuleEvaluator.Calculate(percentage, Context()));
        Assert.Equal(100m, RegistrationRuleEvaluator.Calculate(tiered, Context()));
        Assert.Equal(480_700m, RegistrationRuleEvaluator.Calculate(formula, Context()));
    }

    [Fact]
    public void EffectiveToIsExclusiveAndFutureRuleStartsExactlyAtBoundary()
    {
        var boundary = new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.FromHours(7));
        var current = Rule(RegistrationComponent.FirstRegistrationTax, CalculationType.Percentage, "{\"rate\":0}", to: boundary);
        var future = Rule(RegistrationComponent.FirstRegistrationTax, CalculationType.Percentage, "{\"rate\":0}", from: boundary, version: 2);

        var result = Assert.Single(RegistrationRuleEvaluator.Evaluate([current, future], Context(), boundary));
        Assert.Equal(2, result.Rule.Version);
    }

    [Fact]
    public void UnknownNumericInputDoesNotMatchAConstrainedRule()
    {
        var context = Context() with { Seats = null };
        var rule = Rule(RegistrationComponent.PlateAndRegistrationFee, CalculationType.Fixed, "{\"amount\":140000}", scope: "{\"all\":[{\"field\":\"seats\",\"operator\":\"lte\",\"value\":9}]}");

        Assert.Empty(RegistrationRuleEvaluator.Evaluate([rule], context, Now));
    }

    private static RegistrationRuleContext Context() => new("VN-01", "I", "PassengerCar", 5, "Bev", "Individual", true, 12, 646_000_000m);

    private static RegistrationRule Rule(
        RegistrationComponent component,
        CalculationType type,
        string parameters,
        int priority = 1,
        string scope = "{}",
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int version = 1) => new()
        {
            Component = component,
            CalculationType = type,
            ParametersJson = parameters,
            ScopeJson = scope,
            Priority = priority,
            Version = version,
            EffectiveFrom = from ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo = to,
            ManualOverrideReason = "Unit test",
        };
}
