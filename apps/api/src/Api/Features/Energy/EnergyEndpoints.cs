using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Energy;

public static class EnergyEndpoints
{
    public static IEndpointRouteBuilder MapEnergyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/calculators/energy", async (
                EnergyCalculationRequest request,
                IEnergyService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.CalculateAsync(request, cancellationToken));
                }
                catch (EnergyCalculationException exception)
                {
                    return Results.Json(
                        new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
                        statusCode: exception.StatusCode);
                }
            })
            .WithName("CalculateMonthlyEnergyCost")
            .WithTags("Energy")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<EnergyCalculationResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
