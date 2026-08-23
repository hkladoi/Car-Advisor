using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietnamCarPlatform.Infrastructure.Persistence;

namespace VietnamCarPlatform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["DATABASE_URL"]
            ?? configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("DATABASE_URL or ConnectionStrings:Postgres is required.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
