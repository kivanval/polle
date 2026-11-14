using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Polle.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");
        // Public/liveness endpoints work before configuration; database operations fail closed.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Database=polle;Username=polle;Timeout=3";
        }

        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(DatabaseConnection.Normalize(connectionString), npgsql => npgsql.UseNetTopologySuite().MigrationsHistoryTable("__ecocrop_migrations", "public"))
                .UseSnakeCaseNamingConvention());

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return services;
    }
}
