namespace VietnamCarPlatform.Domain.Admin;

public sealed record DealerOfferQualityInput(
    Guid OfferId,
    Guid BranchId,
    string BranchProvinceCode,
    Guid TrimId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string Status,
    string ConditionsJson,
    bool HasProvenance,
    IReadOnlyList<DealerOfferBenefitQualityInput> Benefits);

public sealed record DealerOfferBenefitQualityInput(
    Guid BenefitId,
    string Type,
    string? ExclusivityGroup,
    decimal? CashValue,
    decimal? StatedValue);

public sealed record DataQualityFinding(
    string Code,
    string Severity,
    string EntityType,
    Guid EntityId,
    string FieldPath,
    string Message);

public static class DealerOfferQualityEvaluator
{
    public static IReadOnlyList<DataQualityFinding> Evaluate(DealerOfferQualityInput offer, DateTimeOffset now)
    {
        var findings = new List<DataQualityFinding>();
        if (string.Equals(offer.Status, "Published", StringComparison.OrdinalIgnoreCase)
            && offer.EffectiveTo is not null
            && offer.EffectiveTo <= now)
        {
            findings.Add(Finding("DEALER_OFFER_EXPIRED", "High", "effectiveTo", "Published dealer offer has expired."));
        }
        if (!offer.HasProvenance && string.Equals(offer.Status, "Published", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Finding("DEALER_OFFER_PROVENANCE_MISSING", "High", "sourceFactId", "Published dealer offer has no source fact or reviewed override."));
        }

        foreach (var duplicate in offer.Benefits
                     .GroupBy(value => $"{value.Type}\u001f{value.ExclusivityGroup ?? string.Empty}", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            findings.Add(Finding("DEALER_OFFER_DUPLICATE_BENEFIT", "Medium", "benefits", $"Duplicate {duplicate.First().Type} benefit in the same exclusivity scope."));
        }

        foreach (var conflict in offer.Benefits
                     .Where(value => !string.IsNullOrWhiteSpace(value.ExclusivityGroup))
                     .GroupBy(value => value.ExclusivityGroup!, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            findings.Add(Finding("DEALER_OFFER_EXCLUSIVITY_CONFLICT", "High", "benefits.exclusivityGroup", $"Multiple benefits occupy exclusivity group '{conflict.Key}'."));
        }

        if (TryReadProvinceCondition(offer.ConditionsJson, out var province)
            && !string.Equals(province, offer.BranchProvinceCode, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Finding("DEALER_OFFER_REGION_BRANCH_MISMATCH", "High", "conditions.provinceCode", $"Offer province '{province}' does not match branch province '{offer.BranchProvinceCode}'."));
        }

        return findings;

        DataQualityFinding Finding(string code, string severity, string field, string message) =>
            new(code, severity, "DealerOffer", offer.OfferId, field, message);
    }

    private static bool TryReadProvinceCondition(string json, out string? province)
    {
        province = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return Find(document.RootElement, out province);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        static bool Find(System.Text.Json.JsonElement element, out string? value)
        {
            value = null;
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals("provinceCode", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        value = property.Value.GetString();
                        return !string.IsNullOrWhiteSpace(value);
                    }
                    if (Find(property.Value, out value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (Find(item, out value))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
