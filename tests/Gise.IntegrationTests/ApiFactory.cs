using Gise.Infrastructure.Payments;
using Gise.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gise.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL, shared by the whole collection, on a clock tests can move.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";
    public const string WebhookSecret = "integration-tests-webhook-secret";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Tests run one after another in this collection, so moving the clock affects only the test that does it.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string ConnectionString => _database.GetConnectionString();

    public FakeGateway Payments { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Gise", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:AttemptsPerMinute", "100000");
        builder.UseSetting("Payments:WebhookSecret", WebhookSecret);
        builder.UseSetting("Payments:ApiKey", "not-used");
        builder.UseSetting("Sales:HoldsPerMinute", "100000");
        // No background sweep: tests sweep when they choose to, so what they see does not depend on timing.
        builder.UseSetting("Sales:ReconcileSeconds", "0");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Payments);
        });
    }

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue($"p{i}", parameters[i]);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    /// <summary>Sweeps until there is nothing left to do, so leftovers from earlier tests never crowd out this one's.</summary>
    public async Task ReconcileAsync()
    {
        var reconciler = Services.GetRequiredService<Api.Features.Sales.SalesReconciler>();
        for (var i = 0; i < 20; i++)
        {
            var result = await reconciler.ReconcileOnceAsync(CancellationToken.None);
            if (result == new Api.Features.Sales.ReconcileResult(0, 0, 0, 0))
            {
                return;
            }
        }
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
