using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietnamCarPlatform.Domain.Admin;
using VietnamCarPlatform.Domain.Sources;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Api.Features.Admin;

public interface IAdminManualImportService
{
    Task<IReadOnlyList<AdminManualImportResponse>> GetAsync(CancellationToken cancellationToken);
    Task<AdminManualImportResponse> ValidateAsync(AdminManualImportRequest request, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
    Task<AdminManualImportResponse> StageAsync(Guid id, string reason, AdminActor actor, HttpContext context, CancellationToken cancellationToken);
}

public sealed class AdminManualImportService(AppDbContext database, TimeProvider timeProvider) : IAdminManualImportService
{
    private static readonly string[] IdentityFields = ["brand_slug", "model_slug", "generation_code", "model_year", "trim_slug"];
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AdminManualImportResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var imports = await database.ManualImports.AsNoTracking()
            .OrderByDescending(value => value.SubmittedAt)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        return imports.Select(ToResponse).ToArray();
    }

    public async Task<AdminManualImportResponse> ValidateAsync(
        AdminManualImportRequest request,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(request.Reason);
        if (Encoding.UTF8.GetByteCount(request.Content) > 2_000_000)
        {
            throw new AdminOperationException(413, "ADMIN_IMPORT_TOO_LARGE", "Manual import content exceeds the 2 MB V1 limit.");
        }
        var parsed = AdminManualImportValidator.Parse(request.FileName, request.Content);
        var sourceUrls = parsed.Records
            .Select(value => value.GetValueOrDefault("source_url"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var registered = await database.Sources.AsNoTracking()
            .Where(value => sourceUrls.Contains(value.Url))
            .Select(value => value.Url)
            .ToArrayAsync(cancellationToken);
        var registeredSet = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = parsed.Issues.ToList();
        for (var index = 0; index < parsed.Records.Count; index++)
        {
            var sourceUrl = parsed.Records[index].GetValueOrDefault("source_url");
            if (!string.IsNullOrWhiteSpace(sourceUrl) && !registeredSet.Contains(sourceUrl))
            {
                issues.Add(new AdminImportValidationIssue(index + 2, "source_url", "SOURCE_NOT_REGISTERED", "Error", "Source URL must exist in the reviewed source registry before staging."));
            }
        }
        var status = issues.Any(issue => issue.Severity == "Error") ? ManualImportStatus.Invalid : ManualImportStatus.Validated;
        var now = timeProvider.GetUtcNow();
        var report = JsonSerializer.Serialize(new { recordCount = parsed.Records.Count, issues });
        var import = new ManualImport
        {
            FileName = Path.GetFileName(request.FileName),
            Format = parsed.Format,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content))).ToLowerInvariant(),
            ContentText = request.Content,
            Status = status,
            ValidationReportJson = report,
            SubmittedBy = actor.Email,
            Reason = request.Reason.Trim(),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.ManualImports.Add(import);
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "ManualImportValidated",
            "ManualImport",
            import.Id,
            null,
            new { import.FileName, import.ContentHash, Status = import.Status.ToString(), RecordCount = parsed.Records.Count, IssueCount = issues.Count },
            request.Reason,
            context,
            now));
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(import);
    }

    public async Task<AdminManualImportResponse> StageAsync(
        Guid id,
        string reason,
        AdminActor actor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        AdminCatalogService.ValidateReason(reason);
        var import = await database.ManualImports.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new AdminOperationException(404, "ADMIN_IMPORT_NOT_FOUND", "Manual import was not found.");
        if (import.Status != ManualImportStatus.Validated)
        {
            throw new AdminOperationException(409, "ADMIN_IMPORT_NOT_VALIDATED", "Only a clean validated import can be staged for review.");
        }
        var parsed = AdminManualImportValidator.Parse(import.FileName, import.ContentText);
        if (parsed.Issues.Any(issue => issue.Severity == "Error"))
        {
            throw new AdminOperationException(409, "ADMIN_IMPORT_CHANGED", "Stored import no longer passes the current validator.");
        }
        var now = timeProvider.GetUtcNow();
        foreach (var record in parsed.Records)
        {
            var identity = string.Join("|", IdentityFields
                .Select(key => record.GetValueOrDefault(key) ?? string.Empty));
            var entityId = StableGuid(identity);
            database.DataChanges.Add(new DataChange
            {
                EntityType = "ManualVehicleImport",
                EntityId = entityId,
                FieldPath = $"manual-import:{import.Id}",
                NewValue = JsonSerializer.Serialize(record),
                RiskLevel = ChangeRiskLevel.High,
                Status = ChangeStatus.PendingReview,
                DetectedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        import.Status = ManualImportStatus.StagedForReview;
        import.StagedAt = now;
        import.UpdatedAt = now;
        database.AuditEvents.Add(AdminCatalogService.Audit(
            actor,
            "ManualImportStaged",
            "ManualImport",
            import.Id,
            new { Status = ManualImportStatus.Validated.ToString() },
            new { Status = import.Status.ToString(), Changes = parsed.Records.Count },
            reason,
            context,
            now));
        await database.SaveChangesAsync(cancellationToken);
        return ToResponse(import);
    }

    private static AdminManualImportResponse ToResponse(ManualImport import)
    {
        using var report = JsonDocument.Parse(import.ValidationReportJson);
        var recordCount = report.RootElement.GetProperty("recordCount").GetInt32();
        var issues = report.RootElement.GetProperty("issues").Deserialize<AdminImportValidationIssue[]>(WebJson) ?? [];
        return new AdminManualImportResponse(import.Id, import.FileName, import.Format, import.Status.ToString(), import.ContentHash, recordCount, issues, import.SubmittedAt, import.StagedAt);
    }

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed record ParsedManualImport(
    string Format,
    IReadOnlyList<Dictionary<string, string>> Records,
    IReadOnlyList<AdminImportValidationIssue> Issues);

public static class AdminManualImportValidator
{
    private static readonly string[] Required =
    [
        "brand_name", "brand_slug", "model_name", "model_slug", "generation_code", "model_year",
        "trim_name", "trim_slug", "source_url", "body_type", "segment", "market_status", "powertrain", "price_type",
    ];
    private static readonly string[] MarketStatuses = ["Active", "Upcoming", "Announced", "Discontinued", "Unknown"];
    private static readonly string[] Powertrains = ["Ice", "Hev", "Phev", "Erev", "Bev", "Unknown"];
    private static readonly string[] IdentityFields = ["brand_slug", "model_slug", "generation_code", "model_year", "trim_slug"];

    public static ParsedManualImport Parse(string fileName, string content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        IReadOnlyList<Dictionary<string, string>> records;
        var issues = new List<AdminImportValidationIssue>();
        try
        {
            records = extension switch
            {
                ".csv" => ParseCsv(content),
                ".json" => ParseJson(content),
                _ => throw new FormatException("Only .csv and .json manual imports are accepted."),
            };
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            issues.Add(new AdminImportValidationIssue(null, "file", "IMPORT_PARSE_ERROR", "Error", exception.Message));
            return new ParsedManualImport(extension.TrimStart('.'), [], issues);
        }
        if (records.Count == 0)
        {
            issues.Add(new AdminImportValidationIssue(null, "file", "IMPORT_EMPTY", "Error", "Import contains no records."));
        }
        if (records.Count > 500)
        {
            issues.Add(new AdminImportValidationIssue(null, "file", "IMPORT_TOO_MANY_ROWS", "Error", "V1 manual imports are limited to 500 records per review batch."));
        }
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < records.Count; index++)
        {
            var row = index + 2;
            var record = records[index];
            foreach (var field in Required.Where(field => string.IsNullOrWhiteSpace(record.GetValueOrDefault(field))))
            {
                issues.Add(new AdminImportValidationIssue(row, field, "CORE_FIELD_MISSING", "Error", "Required import field is blank."));
            }
            ValidateHttps(record, row, "source_url", issues);
            ValidateHttps(record, row, "brand_official_url", issues, required: false);
            ValidateInteger(record, row, "model_year", 1990, 2100, issues);
            ValidateDecimal(record, row, "seats", 1, 80, issues, false);
            ValidateDecimal(record, row, "length_mm", 2500, 7000, issues, false);
            ValidateDecimal(record, row, "width_mm", 1200, 3000, issues, false);
            ValidateDecimal(record, row, "height_mm", 1000, 3500, issues, false);
            ValidateDecimal(record, row, "wheelbase_mm", 1500, 5000, issues, false);
            ValidateDecimal(record, row, "msrp_amount", 1, 100_000_000_000, issues, !string.Equals(record.GetValueOrDefault("price_type"), "Unannounced", StringComparison.OrdinalIgnoreCase));
            ValidateEnum(record, row, "market_status", MarketStatuses, issues);
            ValidateEnum(record, row, "powertrain", Powertrains, issues);

            var identity = string.Join("|", IdentityFields
                .Select(key => record.GetValueOrDefault(key)?.Trim() ?? string.Empty));
            if (!identities.Add(identity))
            {
                issues.Add(new AdminImportValidationIssue(row, "trim_slug", "DUPLICATE_TRIM_IDENTITY", "Error", "Duplicate brand/model/generation/model-year/trim identity in this batch."));
            }
        }
        return new ParsedManualImport(extension.TrimStart('.'), records, issues);
    }

    private static List<Dictionary<string, string>> ParseCsv(string content)
    {
        var rows = CsvRows(content);
        if (rows.Count == 0)
        {
            return [];
        }
        var headers = rows[0].Select(value => value.Trim().ToLowerInvariant()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
        {
            throw new FormatException("CSV headers must be nonblank and unique.");
        }
        var records = new List<Dictionary<string, string>>();
        foreach (var values in rows.Skip(1).Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value))))
        {
            if (values.Count != headers.Length)
            {
                throw new FormatException("Every CSV row must have the same number of columns as the header.");
            }
            records.Add(headers.Select((header, index) => new { header, value = values[index].Trim() })
                .ToDictionary(value => value.header, value => value.value, StringComparer.OrdinalIgnoreCase));
        }
        return records;
    }

    private static Dictionary<string, string>[] ParseJson(string content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var records)
                ? records
                : throw new FormatException("JSON import must be an array or an object with a records array.");
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("JSON records must be an array.");
        }
        return array.EnumerateArray().Select(element =>
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Every JSON record must be an object.");
            }
            return element.EnumerateObject().ToDictionary(
                property => property.Name.ToLowerInvariant(),
                property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }).ToArray();
    }

    private static List<List<string>> CsvRows(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(character);
            }
        }
        if (quoted)
        {
            throw new FormatException("CSV contains an unterminated quoted field.");
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    private static void ValidateHttps(Dictionary<string, string> record, int row, string field, List<AdminImportValidationIssue> issues, bool required = true)
    {
        var value = record.GetValueOrDefault(field);
        if (string.IsNullOrWhiteSpace(value) && !required)
        {
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            issues.Add(new AdminImportValidationIssue(row, field, "HTTPS_URL_REQUIRED", "Error", "Value must be an absolute HTTPS URL."));
        }
    }

    private static void ValidateInteger(Dictionary<string, string> record, int row, string field, int minimum, int maximum, List<AdminImportValidationIssue> issues)
    {
        if (!int.TryParse(record.GetValueOrDefault(field), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value < minimum || value > maximum)
        {
            issues.Add(new AdminImportValidationIssue(row, field, "IMPOSSIBLE_VALUE", "Error", $"Value must be between {minimum} and {maximum}."));
        }
    }

    private static void ValidateDecimal(Dictionary<string, string> record, int row, string field, decimal minimum, decimal maximum, List<AdminImportValidationIssue> issues, bool required)
    {
        var raw = record.GetValueOrDefault(field);
        if (string.IsNullOrWhiteSpace(raw) && !required)
        {
            return;
        }
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value < minimum || value > maximum)
        {
            issues.Add(new AdminImportValidationIssue(row, field, "IMPOSSIBLE_VALUE", "Error", $"Value must be between {minimum} and {maximum}."));
        }
    }

    private static void ValidateEnum(Dictionary<string, string> record, int row, string field, IReadOnlyCollection<string> allowed, List<AdminImportValidationIssue> issues)
    {
        if (!allowed.Contains(record.GetValueOrDefault(field) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new AdminImportValidationIssue(row, field, "CANONICAL_VALUE_REQUIRED", "Error", $"Value must be one of: {string.Join(", ", allowed)}."));
        }
    }
}
