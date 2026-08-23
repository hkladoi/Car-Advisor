using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Financing;

public static class FinancingEndpoints
{
    public static IEndpointRouteBuilder MapFinancingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/financing/calculate", async (
                FinancingCalculationRequest request,
                IFinancingService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.CalculateAsync(request, cancellationToken));
                }
                catch (FinancingCalculationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("CalculatePurchaseFinancing")
            .WithTags("Financing")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<FinancingCalculationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
