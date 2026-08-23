using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Compare;

public static class CompareEndpoints
{
    public static IEndpointRouteBuilder MapCompareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/compare/calculate", async (
                CompareCalculationRequest request,
                ICompareService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.CalculateAsync(request, cancellationToken));
                }
                catch (CompareCalculationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("CompareTrims")
            .WithTags("Compare")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<CompareCalculationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
