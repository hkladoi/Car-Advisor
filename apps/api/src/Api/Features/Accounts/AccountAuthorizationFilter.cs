using VietnamCarPlatform.Api.Models;

namespace VietnamCarPlatform.Api.Features.Accounts;

public sealed class AccountAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var service = http.RequestServices.GetRequiredService<IAccountAuthService>();
        var actor = await service.AuthenticateAsync(http, http.RequestAborted);
        if (actor is null)
        {
            return Results.Json(
                new ApiError("ACCOUNT_AUTH_REQUIRED", "A valid opt-in account session is required.", [], http.TraceIdentifier),
                statusCode: StatusCodes.Status401Unauthorized);
        }
        http.Items[typeof(AccountActor)] = actor;
        http.Response.Headers.CacheControl = "no-store, private";
        return await next(context);
    }
}

public static class AccountAuthorizationExtensions
{
    public static RouteHandlerBuilder RequireAccount(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<AccountAuthorizationFilter>()
            .Produces(StatusCodes.Status401Unauthorized);

    public static AccountActor AccountActor(this HttpContext context) =>
        (AccountActor)(context.Items[typeof(AccountActor)]
            ?? throw new InvalidOperationException("Account authorization filter did not run."));
}
