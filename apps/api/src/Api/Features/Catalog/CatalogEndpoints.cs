using Microsoft.AspNetCore.Mvc;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Catalog;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1")
            .WithTags("Catalog");

        group.MapGet("/brands", async (
                ICatalogService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetBrandsAsync(cancellationToken)))
            .WithName("GetBrands")
            .Produces<BrandsResponse>();

        group.MapGet("/cars", async (
                [AsParameters] CatalogRequest request,
                ICatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                if (!CatalogFilter.TryCreate(request, out var filter, out var errors))
                {
                    var fieldErrors = errors
                        .SelectMany(error => error.Value.Select(code => new FieldError(error.Key, code)))
                        .ToArray();
                    return Results.Json(
                        new ApiError("CATALOG_FILTER_INVALID", "One or more catalog filters are invalid.", fieldErrors, context.TraceIdentifier),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                return Results.Ok(await service.GetCarsAsync(filter!, cancellationToken));
            })
            .WithName("GetCars")
            .Produces<CarsResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        group.MapGet("/cars/{trimId:guid}", async (
                Guid trimId,
                ICatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var car = await service.GetCarAsync(trimId, cancellationToken);
                return car is null
                    ? Results.Json(
                        new ApiError("CATALOG_TRIM_NOT_FOUND", "The requested Vietnam-market trim was not found.", [], context.TraceIdentifier),
                        statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(car);
            })
            .WithName("GetCarByTrimId")
            .Produces<CarDetailResponse>()
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
