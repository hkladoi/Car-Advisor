using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Accounts;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/accounts").WithTags("Accounts");

        group.MapPost("/register", async (
                AccountRegisterRequest request,
                IAccountAuthService auth,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created("/api/v1/accounts/me", await auth.RegisterAsync(request, context, cancellationToken)))
            .WithName("RegisterAccount")
            .RequireRateLimiting("account-auth")
            .Produces<AccountAuthResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
                AccountLoginRequest request,
                IAccountAuthService auth,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var result = await auth.LoginAsync(request, context, cancellationToken);
                return result is null
                    ? Results.Json(new ApiError("ACCOUNT_LOGIN_INVALID", "Email, password or account state is invalid.", [], context.TraceIdentifier), statusCode: StatusCodes.Status401Unauthorized)
                    : Results.Ok(result);
            })
            .WithName("LoginAccount")
            .RequireRateLimiting("account-auth")
            .Produces<AccountAuthResponse>()
            .Produces<ApiError>(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", async (
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSessionAsync(context.AccountActor(), cancellationToken)))
            .WithName("GetAccountSession")
            .RequireAccount()
            .Produces<AccountSessionResponse>();

        group.MapPost("/logout", async (
                IAccountAuthService auth,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await auth.LogoutAsync(context.AccountActor(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("LogoutAccount")
            .RequireAccount()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/profile", async (
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var profile = await service.GetProfileAsync(context.AccountActor(), cancellationToken);
                return profile is null ? Results.NoContent() : Results.Ok(profile);
            })
            .WithName("GetAccountProfile")
            .RequireAccount()
            .Produces<AccountProfileResponse>()
            .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/profile", async (
                AccountProfileRequest request,
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.SaveProfileAsync(context.AccountActor(), request, cancellationToken)))
            .WithName("SaveAccountProfile")
            .RequireAccount()
            .Produces<AccountProfileResponse>();

        group.MapGet("/comparisons", async (
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetComparisonsAsync(context.AccountActor(), cancellationToken)))
            .WithName("GetSavedComparisons")
            .RequireAccount()
            .Produces<IReadOnlyList<SavedComparisonResponse>>();

        group.MapPost("/comparisons", async (
                SavedComparisonRequest request,
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Created("/api/v1/accounts/comparisons", await service.SaveComparisonAsync(context.AccountActor(), request, cancellationToken)))
            .WithName("SaveComparison")
            .RequireAccount()
            .Produces<SavedComparisonResponse>(StatusCodes.Status201Created);

        group.MapDelete("/comparisons/{id:guid}", async (
                Guid id,
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteComparisonAsync(context.AccountActor(), id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteSavedComparison")
            .RequireAccount()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/watchlist", async (
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetWatchlistAsync(context.AccountActor(), cancellationToken)))
            .WithName("GetWatchlist")
            .RequireAccount()
            .Produces<IReadOnlyList<WatchlistResponse>>();

        group.MapPut("/watchlist", async (
                WatchlistRequest request,
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.SaveWatchlistAsync(context.AccountActor(), request, cancellationToken)))
            .WithName("SaveWatchlist")
            .RequireAccount()
            .Produces<WatchlistResponse>();

        group.MapDelete("/watchlist/{trimId:guid}", async (
                Guid trimId,
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await service.DeleteWatchlistAsync(context.AccountActor(), trimId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteWatchlist")
            .RequireAccount()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/alerts", async (
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAlertsAsync(context.AccountActor(), cancellationToken)))
            .WithName("GetAccountAlerts")
            .RequireAccount()
            .Produces<IReadOnlyList<AccountAlertResponse>>();

        group.MapGet("/export", async (
                IAccountService service,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.ExportAsync(context.AccountActor(), cancellationToken)))
            .WithName("ExportAccountData")
            .RequireAccount()
            .Produces<AccountDataExportResponse>();

        group.MapDelete("/me", async (
                [FromBody] AccountDeleteRequest request,
                IAccountAuthService auth,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                await auth.DeleteAsync(context.AccountActor(), request, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteAccount")
            .RequireAccount()
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }
}
