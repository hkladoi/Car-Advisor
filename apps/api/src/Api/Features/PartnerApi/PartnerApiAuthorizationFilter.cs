using System.Globalization;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.PartnerApi;

public sealed class PartnerApiAuthorizationFilter : IEndpointFilter
{
    private static readonly Action<ILogger, Exception?> RateCounterUnavailable = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1351, nameof(RateCounterUnavailable)),
        "Partner API rate counter is unavailable; request denied closed");

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
        {
            return Error(
                http,
                StatusCodes.Status405MethodNotAllowed,
                "PARTNER_API_READ_ONLY",
                "Partner API keys authorize read-only GET and HEAD requests only.");
        }
        if (!http.Request.Headers.TryGetValue("X-VCP-API-Key", out var values) || values.Count != 1)
        {
            return Error(
                http,
                StatusCodes.Status401Unauthorized,
                "PARTNER_API_KEY_REQUIRED",
                "A valid X-VCP-API-Key header is required.");
        }

        var service = http.RequestServices.GetRequiredService<IPartnerApiService>();
        var access = await service.AuthenticateAsync(values.ToString(), http.RequestAborted);
        if (access is null)
        {
            return Error(
                http,
                StatusCodes.Status401Unauthorized,
                "PARTNER_API_KEY_INVALID",
                "The partner API key is invalid, expired, revoked or bound to an older policy.");
        }

        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers["X-VCP-Contract-Version"] = PartnerApiPolicy.ContractVersion;
        http.Response.Headers["X-VCP-Data-Policy-Version"] = PartnerApiPolicy.PolicyVersion;
        http.Response.Headers.Link = $"<{PartnerApiPolicy.PolicyPath}>; rel=\"describedby\"";
        if (http.Request.Query.TryGetValue("pageSize", out var requestedPageSize)
            && int.TryParse(requestedPageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageSize)
            && pageSize > access.MaxPageSize)
        {
            return Results.Json(
                new ApiError(
                    "PARTNER_API_PLAN_PAGE_SIZE_EXCEEDED",
                    $"The {access.PlanCode} plan permits at most {access.MaxPageSize} rows per page.",
                    [new FieldError("pageSize", "PLAN_LIMIT_EXCEEDED")],
                    http.TraceIdentifier),
                statusCode: StatusCodes.Status403Forbidden);
        }

        PartnerRateLimitDecision decision;
        try
        {
            decision = await http.RequestServices.GetRequiredService<IPartnerApiRateCounter>()
                .AcquireAsync(
                    access,
                    http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
                    http.RequestAborted);
        }
        catch (RedisException error)
        {
            RateCounterUnavailable(
                http.RequestServices.GetRequiredService<ILogger<PartnerApiAuthorizationFilter>>(),
                error);
            return Error(
                http,
                StatusCodes.Status503ServiceUnavailable,
                "PARTNER_API_RATE_COUNTER_UNAVAILABLE",
                "Partner API usage enforcement is temporarily unavailable.");
        }

        ApplyHeaders(http, decision);
        if (!decision.Allowed)
        {
            return Error(
                http,
                StatusCodes.Status429TooManyRequests,
                "PARTNER_API_RATE_LIMITED",
                "The API key usage plan limit has been reached.");
        }

        http.Items[typeof(PartnerApiAccess)] = access;
        http.Items[typeof(PartnerRateLimitDecision)] = decision;
        return await next(context);
    }

    private static void ApplyHeaders(HttpContext context, PartnerRateLimitDecision decision)
    {
        context.Response.Headers["RateLimit-Limit"] = decision.MinuteLimit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["RateLimit-Remaining"] = decision.MinuteRemaining.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["RateLimit-Reset"] = decision.MinuteResetsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Month-Limit"] = decision.MonthLimit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Month-Remaining"] = decision.MonthRemaining.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Month-Reset"] = decision.MonthResetsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (!decision.Allowed)
        {
            context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static IResult Error(HttpContext context, int status, string code, string message)
    {
        if (string.IsNullOrEmpty(context.Response.Headers.CacheControl))
        {
            context.Response.Headers.CacheControl = "no-store";
        }
        return Results.Json(new ApiError(code, message, [], context.TraceIdentifier), statusCode: status);
    }
}

public static class PartnerApiAuthorizationExtensions
{
    public static RouteHandlerBuilder RequirePartnerApiKey(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new PartnerApiAuthorizationFilter())
            .WithOpenApi(operation =>
            {
                operation.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "PartnerApiKey",
                            },
                        }] = Array.Empty<string>(),
                    },
                ];
                return operation;
            })
            .Produces<ApiError>(StatusCodes.Status401Unauthorized)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status429TooManyRequests)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);

    public static PartnerApiAccess PartnerApiAccess(this HttpContext context) =>
        (PartnerApiAccess)(context.Items[typeof(PartnerApiAccess)]
            ?? throw new InvalidOperationException("Partner API authorization filter did not run."));
}
