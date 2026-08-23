using Microsoft.AspNetCore.Mvc;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Pricing;

public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/cars/{trimId:guid}/prices", async (
                Guid trimId,
                [AsParameters] VehiclePriceHistoryQuery query,
                IHistoryService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.GetVehiclePricesAsync(trimId, query, cancellationToken));
                }
                catch (HistoryOperationException exception)
                {
                    return Error(context, exception);
                }
            })
            .WithName("GetVehiclePriceHistory")
            .WithTags("Pricing history")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<VehiclePriceHistoryResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/v1/cars/{trimId:guid}/dealer-offers", async (
                Guid trimId,
                [AsParameters] DealerOfferHistoryQuery query,
                IHistoryService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.GetDealerOffersAsync(trimId, query, cancellationToken));
                }
                catch (HistoryOperationException exception)
                {
                    return Error(context, exception);
                }
            })
            .WithName("GetVehicleDealerOfferHistory")
            .WithTags("Pricing history")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<DealerOfferHistoryResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/v1/energy/prices/history", async (
                [AsParameters] EnergyPriceHistoryQuery query,
                IHistoryService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.GetEnergyPricesAsync(query, cancellationToken));
                }
                catch (HistoryOperationException exception)
                {
                    return Error(context, exception);
                }
            })
            .WithName("GetEnergyPriceHistory")
            .WithTags("Energy history")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<EnergyPriceHistoryResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static IResult Error(HttpContext context, HistoryOperationException exception) =>
        Results.Json(
            new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
            statusCode: exception.StatusCode);
}
