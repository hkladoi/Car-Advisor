using VietnamCarPlatform.Api.Models;
using VietnamCarPlatform.Domain.Admin;

namespace VietnamCarPlatform.Api.Features.Admin;

public sealed class AdminAuthorizationFilter(AdministratorRole minimumRole) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var service = http.RequestServices.GetRequiredService<IAdminAuthService>();
        var actor = await service.AuthenticateAsync(http, http.RequestAborted);
        if (actor is null)
        {
            return Results.Json(
                new ApiError("ADMIN_AUTH_REQUIRED", "A valid administrator session is required.", [], http.TraceIdentifier),
                statusCode: StatusCodes.Status401Unauthorized);
        }
        if (actor.Role < minimumRole)
        {
            return Results.Json(
                new ApiError("ADMIN_ROLE_FORBIDDEN", "The administrator role cannot perform this action.", [], http.TraceIdentifier),
                statusCode: StatusCodes.Status403Forbidden);
        }
        http.Items[typeof(AdminActor)] = actor;
        http.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }
}

public static class AdminAuthorizationExtensions
{
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder builder, AdministratorRole minimumRole) =>
        builder.AddEndpointFilter(new AdminAuthorizationFilter(minimumRole))
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

    public static AdminActor AdminActor(this HttpContext context) =>
        (AdminActor)(context.Items[typeof(AdminActor)] ?? throw new InvalidOperationException("Admin authorization filter did not run."));
}
