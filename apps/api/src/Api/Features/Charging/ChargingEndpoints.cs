using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Charging;

public static class ChargingEndpoints
{
    public static IEndpointRouteBuilder MapChargingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/charging/stations", async (
                [AsParameters] ChargingStationQuery query,
                IChargingService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.SearchAsync(query, cancellationToken));
                }
                catch (ChargingIntegrationException exception)
                {
                    return Error(context, exception);
                }
            })
            .WithName("SearchCachedChargingStations")
            .WithTags("Charging")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<ChargingStationListResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        endpoints.MapGet("/api/v1/maps/geocode", async (
                string address,
                IGoongGeocodingClient client,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await client.ForwardAsync(address, cancellationToken));
                }
                catch (ChargingIntegrationException exception)
                {
                    return Error(context, exception);
                }
            })
            .WithName("GeocodeAddressWithOptionalGoong")
            .WithTags("Maps")
            .RequireRateLimiting("map-geocode")
            .Produces<GeocodeResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet("/api/v1/maps/capabilities", (
                IGoongGeocodingClient client) => Results.Ok(new MapCapabilitiesResponse(
                    true,
                    client.Enabled,
                    client.MapTilesConfigured,
                    false,
                    "Cached charging coordinates with optional server-side Goong geocoding",
                    "Text addresses and cached Open Charge Map coordinates remain available without Goong")))
            .WithName("GetMapCapabilities")
            .WithTags("Maps")
            .Produces<MapCapabilitiesResponse>();

        return endpoints;
    }

    private static IResult Error(HttpContext context, ChargingIntegrationException exception) =>
        Results.Json(
            new ApiError(exception.Code, exception.Message, [], context.TraceIdentifier),
            statusCode: exception.StatusCode);
}
