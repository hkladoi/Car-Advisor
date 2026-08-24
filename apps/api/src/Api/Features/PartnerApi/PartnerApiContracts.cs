using VietnamCarPlatform.Api.Features.Catalog;

namespace VietnamCarPlatform.Api.Features.PartnerApi;

public static class PartnerApiPolicy
{
    public const string ContractVersion = "v1";
    public const string PolicyVersion = "2026-08-24";
    public const string ReadScope = "catalog.read";
    public const string LicenseId = "SOURCE-SPECIFIC";
    public const string PolicyPath = "/api/v1/partner/policy";
    public const string Attribution =
        "Attribute Vietnam Car Platform and preserve every per-record source name, URL and attribution supplied by the API.";
}

public sealed record PartnerApiUsagePlanResponse(
    string Code,
    string Name,
    int RequestsPerMinute,
    long RequestsPerMonth,
    int MaxPageSize);

public sealed record PartnerApiPolicyResponse(
    string ContractVersion,
    string PolicyVersion,
    string Scope,
    string License,
    bool AttributionRequired,
    string Attribution,
    string PolicyDocument,
    IReadOnlyList<string> PermittedUses,
    IReadOnlyList<string> ProhibitedUses,
    IReadOnlyList<PartnerApiUsagePlanResponse> UsagePlans,
    DateTimeOffset GeneratedAt);

public sealed record PartnerApiMetadata(
    string ContractVersion,
    string PolicyVersion,
    string License,
    string Attribution,
    string PolicyPath,
    DateTimeOffset GeneratedAt);

public sealed record PartnerBrandsResponse(BrandsResponse Data, PartnerApiMetadata Meta);

public sealed record PartnerCarsResponse(CarsResponse Data, PartnerApiMetadata Meta);

public sealed record PartnerCarResponse(CarDetailResponse Data, PartnerApiMetadata Meta);

public sealed record PartnerCredentialResponse(
    Guid KeyId,
    string Name,
    string KeyPrefix,
    string Scope,
    string PlanCode,
    int RequestsPerMinute,
    long RequestsPerMonth,
    int MaxPageSize,
    DateTimeOffset? ExpiresAt,
    PartnerApiMetadata Meta);

public sealed record AdminPartnerApiKeyCreateRequest(
    string Name,
    string PlanCode,
    string PolicyVersion,
    DateTimeOffset? ExpiresAt,
    string Reason);

public sealed record AdminPartnerApiKeyResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    string Scope,
    string PlanCode,
    string PolicyVersion,
    string Status,
    DateTimeOffset IssuedAt,
    string IssuedBy,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedBy);

public sealed record AdminPartnerApiKeyIssuedResponse(
    AdminPartnerApiKeyResponse Key,
    string ApiKey,
    string SecretHandlingNotice);

public sealed record AdminPartnerApiKeyRevokeRequest(string Reason);
