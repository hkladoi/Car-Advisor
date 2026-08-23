namespace VietnamCarPlatform.Api.Models;

public sealed record FieldError(string Field, string Code);

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyCollection<FieldError> FieldErrors,
    string TraceId);

