using Microsoft.AspNetCore.RateLimiting;
using VietnamCarPlatform.Api.Models;
using VietnamCarPlatform.Domain.Admin;

namespace VietnamCarPlatform.Api.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin").WithTags("Admin");

        group.MapPost("/auth/login", async (
                AdminLoginRequest request,
                IAdminAuthService auth,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var login = await auth.LoginAsync(request, context, cancellationToken);
                return login is null
                    ? Results.Json(new ApiError("ADMIN_LOGIN_INVALID", "Email, password or account state is invalid.", [], context.TraceIdentifier), statusCode: StatusCodes.Status401Unauthorized)
                    : Results.Ok(login);
            })
            .WithName("AdminLogin")
            .RequireRateLimiting("admin-login")
            .Produces<AdminLoginResponse>()
            .Produces<ApiError>(StatusCodes.Status401Unauthorized);

        group.MapGet("/auth/session", (HttpContext context) =>
            {
                var actor = context.AdminActor();
                return Results.Ok(new AdminSessionResponse(actor.UserId, actor.Email, actor.DisplayName, actor.Role.ToString(), actor.ExpiresAt));
            })
            .WithName("GetAdminSession")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<AdminSessionResponse>();

        group.MapPost("/auth/logout", async (
                AdminReasonRequest request,
                IAdminAuthService auth,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                AdminCatalogService.ValidateReason(request.Reason);
                await auth.LogoutAsync(context.AdminActor(), request.Reason, context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("AdminLogout")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/catalog/trims", async (IAdminCatalogService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetTrimsAsync(cancellationToken)))
            .WithName("GetAdminTrims")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminTrimRow>>();

        group.MapPost("/catalog/trims", async (
                AdminTrimDraftRequest request,
                IAdminCatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created($"/api/v1/admin/catalog/trims", await service.CreateTrimAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("CreateAdminTrim")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminTrimRow>(StatusCodes.Status201Created);

        group.MapPut("/catalog/trims/{id:guid}", async (
                Guid id,
                AdminTrimUpdateRequest request,
                IAdminCatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateTrimAsync(id, request, context.AdminActor(), context, cancellationToken)))
            .WithName("UpdateAdminTrim")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminTrimRow>();

        group.MapDelete("/catalog/trims/{id:guid}", async (
                Guid id,
                string reason,
                IAdminCatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteTrimAsync(id, reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteAdminTrim")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/sources", async (IAdminCatalogService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetSourcesAsync(cancellationToken)))
            .WithName("GetAdminSources")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminSourceResponse>>();

        group.MapPost("/sources", async (
                AdminSourceRequest request,
                IAdminCatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created("/api/v1/admin/sources", await service.CreateSourceAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("CreateAdminSource")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminSourceResponse>(StatusCodes.Status201Created);

        group.MapPut("/sources/{id:guid}", async (
                Guid id,
                AdminSourceRequest request,
                IAdminCatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateSourceAsync(id, request, context.AdminActor(), context, cancellationToken)))
            .WithName("UpdateAdminSource")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminSourceResponse>();

        group.MapDelete("/sources/{id:guid}", async (
                Guid id,
                string reason,
                IAdminCatalogService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeactivateSourceAsync(id, reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeactivateAdminSource")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/imports", async (IAdminManualImportService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetAsync(cancellationToken)))
            .WithName("GetAdminImports")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminManualImportResponse>>();

        group.MapPost("/imports/validate", async (
                AdminManualImportRequest request,
                IAdminManualImportService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.ValidateAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("ValidateAdminImport")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminManualImportResponse>();

        group.MapPost("/imports/{id:guid}/stage", async (
                Guid id,
                AdminReasonRequest request,
                IAdminManualImportService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.StageAsync(id, request.Reason, context.AdminActor(), context, cancellationToken)))
            .WithName("StageAdminImport")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces<AdminManualImportResponse>();

        group.MapGet("/review-queue", async (IAdminReviewService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetQueueAsync(cancellationToken)))
            .WithName("GetAdminReviewQueue")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminReviewItem>>();

        group.MapGet("/publications", async (int? take, IAdminReviewService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetPublicationsAsync(take ?? 100, cancellationToken)))
            .WithName("GetAdminPublications")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminPublicationResponse>>();

        group.MapPost("/publications/{id:guid}/rollback", async (
                Guid id,
                AdminRollbackRequest request,
                IAdminReviewService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.RollbackAsync(id, request.Reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RollbackAdminPublication")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/changes/{id:guid}/approve", async (
                Guid id,
                AdminReviewDecisionRequest request,
                IAdminReviewService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DecideAsync(id, true, request, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("ApproveAdminChange")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/changes/{id:guid}/reject", async (
                Guid id,
                AdminReviewDecisionRequest request,
                IAdminReviewService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DecideAsync(id, false, request, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RejectAdminChange")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/changes/{id:guid}/edit-publish", async (
                Guid id,
                AdminReviewDecisionRequest request,
                IAdminReviewService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.EditedValue))
                {
                    throw new AdminOperationException(400, "ADMIN_EDITED_VALUE_REQUIRED", "Edit-and-publish requires an explicit reviewed value.");
                }
                await service.DecideAsync(id, true, request, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("EditAndPublishAdminChange")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/overrides", async (
                AdminOverrideRequest request,
                IAdminReviewService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.OverrideAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("CreateAdminOverride")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces<AdminFieldLockResponse?>();

        group.MapGet("/field-locks", async (IAdminReviewService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetLocksAsync(cancellationToken)))
            .WithName("GetAdminFieldLocks")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminFieldLockResponse>>();

        group.MapPost("/field-locks/{id:guid}/unlock", async (
                Guid id,
                AdminReasonRequest request,
                IAdminReviewService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.UnlockAsync(id, request.Reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("UnlockAdminField")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/dealers", async (IAdminDealerService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetDealersAsync(cancellationToken)))
            .WithName("GetAdminDealers")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminDealerResponse>>();

        group.MapPost("/dealers", async (
                AdminDealerRequest request,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created("/api/v1/admin/dealers", await service.CreateDealerAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("CreateAdminDealer")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminDealerResponse>(StatusCodes.Status201Created);

        group.MapPut("/dealers/{id:guid}", async (
                Guid id,
                AdminDealerRequest request,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateDealerAsync(id, request, context.AdminActor(), context, cancellationToken)))
            .WithName("UpdateAdminDealer")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminDealerResponse>();

        group.MapDelete("/dealers/{id:guid}", async (
                Guid id,
                string reason,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteDealerAsync(id, reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteAdminDealer")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/dealer-branches", async (IAdminDealerService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetBranchesAsync(cancellationToken)))
            .WithName("GetAdminDealerBranches")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminDealerBranchResponse>>();

        group.MapPost("/dealer-branches", async (
                AdminDealerBranchRequest request,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created("/api/v1/admin/dealer-branches", await service.CreateBranchAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("CreateAdminDealerBranch")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminDealerBranchResponse>(StatusCodes.Status201Created);

        group.MapPut("/dealer-branches/{id:guid}", async (
                Guid id,
                AdminDealerBranchRequest request,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateBranchAsync(id, request, context.AdminActor(), context, cancellationToken)))
            .WithName("UpdateAdminDealerBranch")
            .RequireAdmin(AdministratorRole.Editor)
            .Produces<AdminDealerBranchResponse>();

        group.MapDelete("/dealer-branches/{id:guid}", async (
                Guid id,
                string reason,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteBranchAsync(id, reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteAdminDealerBranch")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/dealer-offers", async (IAdminDealerService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetOffersAsync(cancellationToken)))
            .WithName("GetAdminDealerOffers")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminDealerOfferResponse>>();

        group.MapPost("/dealer-offers", async (
                AdminDealerOfferRequest request,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created("/api/v1/admin/dealer-offers", await service.CreateOfferAsync(request, context.AdminActor(), context, cancellationToken)))
            .WithName("CreateAdminDealerOffer")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces<AdminDealerOfferResponse>(StatusCodes.Status201Created);

        group.MapPut("/dealer-offers/{id:guid}", async (
                Guid id,
                AdminDealerOfferRequest request,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateOfferAsync(id, request, context.AdminActor(), context, cancellationToken)))
            .WithName("UpdateAdminDealerOffer")
            .RequireAdmin(AdministratorRole.Reviewer)
            .Produces<AdminDealerOfferResponse>();

        group.MapDelete("/dealer-offers/{id:guid}", async (
                Guid id,
                string reason,
                IAdminDealerService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteOfferAsync(id, reason, context.AdminActor(), context, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteAdminDealerOffer")
            .RequireAdmin(AdministratorRole.Administrator)
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/coverage", async (IAdminQualityService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetCoverageAsync(cancellationToken)))
            .WithName("GetAdminCoverage")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<AdminCoverageResponse>();

        group.MapGet("/quality", async (IAdminQualityService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetQualityAsync(cancellationToken)))
            .WithName("GetAdminQuality")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<AdminQualityResponse>();

        group.MapGet("/audit", async (int? take, IAdminQualityService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetAuditAsync(take ?? 100, cancellationToken)))
            .WithName("GetAdminAudit")
            .RequireAdmin(AdministratorRole.Viewer)
            .Produces<IReadOnlyList<AdminAuditResponse>>();

        return endpoints;
    }
}
