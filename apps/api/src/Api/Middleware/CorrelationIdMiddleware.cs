using System.Diagnostics;

namespace VietnamCarPlatform.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName].FirstOrDefault());
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    public static string ResolveCorrelationId(string? requestedId)
    {
        if (!string.IsNullOrWhiteSpace(requestedId) &&
            requestedId.Length <= 128 &&
            requestedId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            return requestedId;
        }

        return ActivityTraceId.CreateRandom().ToString();
    }
}

