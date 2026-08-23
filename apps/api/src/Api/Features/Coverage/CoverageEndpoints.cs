using VietnamCarPlatform.Api.Features.Admin;

namespace VietnamCarPlatform.Api.Features.Coverage;

public static class CoverageEndpoints
{
    public static IEndpointRouteBuilder MapCoverageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/coverage", async (
                IAdminQualityService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetCoverageAsync(cancellationToken)))
            .WithName("GetPublicCoverage")
            .WithTags("Coverage")
            .RequireRateLimiting("anonymous-heavy")
            .Produces<AdminCoverageResponse>();
        return endpoints;
    }
}
