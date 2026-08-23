using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Registration;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").WithTags("Registration");

        group.MapGet("/regions", async (
                IRegistrationService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetRegionsAsync(cancellationToken)))
            .WithName("GetRegions")
            .Produces<RegionsResponse>();

        group.MapPost("/calculators/on-road", async (
                OnRoadCalculationRequest request,
                IRegistrationService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.CalculateAsync(request, cancellationToken));
                }
                catch (RegistrationCalculationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("CalculateOnRoadPrice")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<OnRoadCalculationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
