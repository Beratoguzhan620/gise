using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGisePersistence(this IServiceCollection services, string connectionString)
    {
        // One data source for EF Core and for the SQL that claims places.
        var dataSource = BuildDataSource(connectionString);
        services.AddSingleton(dataSource);
        services.AddDbContext<GiseDbContext>(options => ConfigureDbContext(options, dataSource));
        return services;
    }

    public static NpgsqlDataSource BuildDataSource(string connectionString) => new NpgsqlDataSourceBuilder(connectionString).Build();

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GiseDbContext>().Database.MigrateAsync(ct);
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
