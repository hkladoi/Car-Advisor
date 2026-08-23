using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VietnamCarPlatform.Api.Features.Affordability;
using VietnamCarPlatform.Api.Features.Admin;
using VietnamCarPlatform.Api.Features.Catalog;
using VietnamCarPlatform.Api.Features.Compare;
using VietnamCarPlatform.Api.Features.Energy;
using VietnamCarPlatform.Api.Features.Financing;
using VietnamCarPlatform.Api.Features.Registration;
using VietnamCarPlatform.Api.Health;
using VietnamCarPlatform.Api.Middleware;
using VietnamCarPlatform.Api.Models;
using VietnamCarPlatform.Infrastructure;
using VietnamCarPlatform.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["SENTRY_DSN"] ?? string.Empty;
    options.SendDefaultPii = false;
    options.TracesSampleRate = 0.1;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["REDIS_URL"] ?? "localhost:6379";
    options.InstanceName = "vcp:";
});
builder.Services.AddScoped<CatalogCache>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IRegistrationService, RegistrationService>();
builder.Services.AddScoped<IEnergyService, EnergyService>();
builder.Services.Configure<AffordabilityOptions>(builder.Configuration.GetSection("Affordability"));
builder.Services.AddScoped<IAffordabilityService, AffordabilityService>();
builder.Services.AddScoped<IFinancingService, FinancingService>();
builder.Services.AddScoped<ICompareService, CompareService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IAdminCatalogService, AdminCatalogService>();
builder.Services.AddScoped<IAdminManualImportService, AdminManualImportService>();
builder.Services.AddScoped<IAdminReviewService, AdminReviewService>();
builder.Services.AddScoped<IAdminQualityService, AdminQualityService>();
builder.Services.AddScoped<IAdminDealerService, AdminDealerService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("admin-login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddPolicy("anonymous-heavy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Vietnam Car Platform API", Version = "v1" });
});

builder.Services.AddHttpClient("readiness", client => client.Timeout = TimeSpan.FromSeconds(2));
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<InfrastructureHealthCheck>("infrastructure", tags: ["ready"]);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("vietnam-car-platform-api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
        }
    });

var app = builder.Build();

if (app.Configuration.GetValue<bool>("APPLY_DATABASE_MIGRATIONS"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.MigrateAsync();
    ApiLog.DatabaseMigrationsApplied(app.Logger);
}

if (!string.IsNullOrWhiteSpace(app.Configuration["ADMIN_BOOTSTRAP_EMAIL"])
    && !string.IsNullOrWhiteSpace(app.Configuration["ADMIN_BOOTSTRAP_PASSWORD"]))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IAdminAuthService>().EnsureBootstrapAsync(CancellationToken.None);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = context.TraceIdentifier;
        var adminError = exception as AdminOperationException;
        context.Response.StatusCode = adminError?.StatusCode ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiError(
            adminError?.Code ?? "UNEXPECTED_ERROR",
            adminError?.Message ?? "The API could not complete the request.",
            [],
            traceId));
        if (adminError is null)
        {
            ApiLog.UnhandledRequestFailure(app.Logger, traceId, exception);
        }
    });
});

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Vietnam Car Platform API v1"));
app.UseRateLimiter();

app.MapGet("/api/v1/system/info", () => Results.Ok(new
    {
        service = "vietnam-car-platform-api",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        architecture = "modular-monolith",
        dataUnit = "trim"
    }))
    .WithName("GetSystemInfo")
    .WithTags("System")
    .Produces(StatusCodes.Status200OK);

app.MapCatalogEndpoints();
app.MapRegistrationEndpoints();
app.MapEnergyEndpoints();
app.MapAffordabilityEndpoints();
app.MapFinancingEndpoints();
app.MapCompareEndpoints();
app.MapAdminEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

public partial class Program;

internal static partial class ApiLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Database migrations applied successfully")]
    public static partial void DatabaseMigrationsApplied(ILogger logger);

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled request failure. TraceId={TraceId}")]
    public static partial void UnhandledRequestFailure(ILogger logger, string traceId, Exception? exception);
}
