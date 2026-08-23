using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Affordability;

public static class AffordabilityEndpoints
{
    public static IEndpointRouteBuilder MapAffordabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/ownership/calculate", async (
                OwnershipCalculationRequest request,
                IAffordabilityService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.CalculateOwnershipAsync(request, cancellationToken));
                }
                catch (OwnershipCalculationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("CalculateOperatingOwnershipCost")
            .WithTags("Ownership")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<OwnershipCalculationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapPost("/api/v1/affordability/evaluate", async (
                AffordabilityEvaluationRequest request,
                IAffordabilityService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.EvaluateAsync(request, cancellationToken));
                }
                catch (OwnershipCalculationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("EvaluateOwnershipAffordability")
            .WithTags("Affordability")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<AffordabilityEvaluationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
