using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Features.Admin;
using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Api.Models;
using VietnamCarPlatform.Domain.Admin;

namespace VietnamCarPlatform.Api.Features.PartnerApi;

public static class PartnerApiEndpoints
{
    public static IEndpointRouteBuilder MapPartnerApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var partner = endpoints.MapGroup("/api/v1/partner").WithTags("Partner API");

        partner.MapGet("/policy", async (
                IPartnerApiService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetPolicyAsync(cancellationToken)))
            .WithName("GetPartnerApiPolicy")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<PartnerApiPolicyResponse>();

        partner.MapGet("/me", (HttpContext context, TimeProvider timeProvider) =>
            {
                var access = context.PartnerApiAccess();
                return Results.Ok(new PartnerCredentialResponse(
                    access.KeyId,
                    access.Name,
                    access.KeyPrefix,
                    access.Scope,
                    access.PlanCode,
                    access.RequestsPerMinute,
                    access.RequestsPerMonth,
                    access.MaxPageSize,
                    access.ExpiresAt,
                    PartnerApiService.Metadata(timeProvider.GetUtcNow())));
            })
            .WithName("GetPartnerCredential")
            .RequirePartnerApiKey()
            .Produces<PartnerCredentialResponse>();

        partner.MapGet("/brands", async (
                ICatalogService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
                Results.Ok(new PartnerBrandsResponse(
                    await service.GetBrandsAsync(cancellationToken),
                    PartnerApiService.Metadata(timeProvider.GetUtcNow()))))
            .WithName("GetPartnerBrands")
            .RequirePartnerApiKey()
            .Produces<PartnerBrandsResponse>();

        partner.MapGet("/cars", async (
                [AsParameters] CatalogRequest request,
                ICatalogService service,
                TimeProvider timeProvider,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                if (!CatalogFilter.TryCreate(request, out var filter, out var errors))
                {
                    return Results.Json(
                        new ApiError(
                            "CATALOG_FILTER_INVALID",
                            "One or more catalog filters are invalid.",
                            errors.SelectMany(error => error.Value.Select(code => new FieldError(error.Key, code))).ToArray(),
                            context.TraceIdentifier),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                return Results.Ok(new PartnerCarsResponse(
                    await service.GetCarsAsync(filter!, cancellationToken),
                    PartnerApiService.Metadata(timeProvider.GetUtcNow())));
            })
            .WithName("GetPartnerCars")
            .RequirePartnerApiKey()
            .Produces<PartnerCarsResponse>()
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        partner.MapGet("/cars/{trimId:guid}", async (
                Guid trimId,
                ICatalogService service,
                TimeProvider timeProvider,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var car = await service.GetCarAsync(trimId, cancellationToken);
                return car is null
                    ? Results.Json(
                        new ApiError(
                            "CATALOG_TRIM_NOT_FOUND",
                            "The requested Vietnam-market trim was not found.",
                            [],
                            context.TraceIdentifier),
                        statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(new PartnerCarResponse(
                        car,
                        PartnerApiService.Metadata(timeProvider.GetUtcNow())));
            })
            .WithName("GetPartnerCarByTrimId")
            .RequirePartnerApiKey()
            .Produces<PartnerCarResponse>()
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        var admin = endpoints.MapGroup("/api/v1/admin/partner-api").WithTags("Admin", "Partner API");

        admin.MapGet("/keys", async (
                IPartnerApiService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetKeysAsync(cancellationToken)))
            .WithName("GetAdminPartnerApiKeys")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminPartnerApiKeyResponse>>();

        admin.MapPost("/keys", async (
                AdminPartnerApiKeyCreateRequest request,
                IPartnerApiService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var issued = await service.IssueKeyAsync(
                    request,
                    context.AdminActor(),
                    context,
                    cancellationToken);
                return Results.Created($"/api/v1/admin/partner-api/keys/{issued.Key.Id}", issued);
            })
            .WithName("IssueAdminPartnerApiKey")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces<AdminPartnerApiKeyIssuedResponse>(StatusCodes.Status201Created);

        admin.MapPost("/keys/{id:guid}/revoke", async (
                Guid id,
                AdminPartnerApiKeyRevokeRequest request,
                IPartnerApiService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.RevokeKeyAsync(
                    id,
                    request.Reason,
                    context.AdminActor(),
                    context,
                    cancellationToken)))
            .WithName("RevokeAdminPartnerApiKey")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces<AdminPartnerApiKeyResponse>();

        return endpoints;
    }
}
