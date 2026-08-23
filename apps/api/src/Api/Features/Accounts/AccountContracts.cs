namespace VietnamCarPlatform.Api.Features.Accounts;

public sealed record AccountRegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    bool PrivacyConsent);

public sealed record AccountLoginRequest(string Email, string Password);

public sealed record AccountDeleteRequest(string Password, string Confirmation);

public sealed record AccountAuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    string DisplayName);

public sealed record AccountSessionResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ConsentedAt,
    string PrivacyPolicyVersion);

public sealed record AccountActor(
    Guid UserId,
    Guid SessionId,
    string Email,
    string DisplayName,
    DateTimeOffset ExpiresAt);

public sealed record AccountProfileRequest(
    string Name,
    string RegionCode,
    decimal NetMonthlyIncome,
    decimal RentHousing,
    decimal EssentialExpenses,
    decimal OtherFixedDebt,
    decimal SavingsTarget,
    decimal MonthlyKilometres,
    decimal ParkingMonthly,
    decimal HouseholdBaseKwh,
    string Policy);

public sealed record AccountProfileResponse(
    Guid Id,
    string Name,
    string RegionCode,
    decimal NetMonthlyIncome,
    decimal RentHousing,
    decimal EssentialExpenses,
    decimal OtherFixedDebt,
    decimal SavingsTarget,
    decimal MonthlyKilometres,
    decimal ParkingMonthly,
    decimal HouseholdBaseKwh,
    string Policy,
    DateTimeOffset UpdatedAt);

public sealed record SavedComparisonRequest(
    string Name,
    IReadOnlyList<Guid> TrimIds,
    string RegionCode,
    string ProfilePreset,
    string FinancingPreset);

public sealed record SavedComparisonResponse(
    Guid Id,
    string Name,
    IReadOnlyList<Guid> TrimIds,
    string RegionCode,
    string ProfilePreset,
    string FinancingPreset,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WatchlistRequest(
    Guid TrimId,
    string RegionCode,
    decimal? TargetPrice,
    bool PriceAlerts,
    bool PromotionAlerts,
    bool DealerOfferAlerts);

public sealed record WatchlistResponse(
    Guid Id,
    Guid TrimId,
    string BrandName,
    string ModelName,
    string TrimName,
    string RegionCode,
    decimal? CurrentPrice,
    decimal? TargetPrice,
    bool PriceAlerts,
    bool PromotionAlerts,
    bool DealerOfferAlerts,
    DateTimeOffset UpdatedAt);

public sealed record AccountAlertSource(
    Guid? SourceFactId,
    string? Name,
    string? Url,
    string? Authority,
    DateTimeOffset? VerifiedAt);

public sealed record AccountAlertResponse(
    string Id,
    string Kind,
    Guid TrimId,
    string Vehicle,
    string Title,
    string Message,
    decimal? Amount,
    string Currency,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    AccountAlertSource Source);

public sealed record AccountDataExportResponse(
    DateTimeOffset ExportedAt,
    AccountSessionResponse Account,
    AccountProfileResponse? Profile,
    IReadOnlyList<SavedComparisonResponse> SavedComparisons,
    IReadOnlyList<WatchlistResponse> Watchlist,
    IReadOnlyList<AccountAlertResponse> CurrentAlerts);

public sealed class AccountOperationException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
