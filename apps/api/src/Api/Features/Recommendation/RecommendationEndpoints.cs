using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Recommendation;

public static class RecommendationEndpoints
{
    public static IEndpointRouteBuilder MapRecommendationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/recommendations", async (
                RecommendationRequest request,
                IRecommendationService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.EvaluateAsync(request, cancellationToken));
                }
                catch (RecommendationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("RecommendTrims")
            .WithTags("Recommendation")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<RecommendationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
