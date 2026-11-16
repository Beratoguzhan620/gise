using System.Net;
using System.Net.Http.Json;
using Gise.Api.Features.Demo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gise.IntegrationTests;

/// <summary>The public demo's set-up, on a database of its own so its sample events stay out of the other tests.</summary>
public sealed class DemoTests : IAsyncLifetime
{
    private readonly ApiFactory _api = new();

    private WebApplicationFactory<Program> _demo = null!;

    public async Task InitializeAsync()
    {
        await _api.InitializeAsync();
        _demo = _api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Demo:Enabled", "true");
            builder.ConfigureTestServices(services => services
                .AddHttpClient(DemoSeeder.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(provider => ((TestServer)provider.GetRequiredService<IServer>()).CreateHandler()));
        });
    }

    // Disposing the factory disposes the demo host made from it, and the database.
    public Task DisposeAsync() => ((IAsyncLifetime)_api).DisposeAsync();

    [Fact]
    public async Task The_demo_puts_events_on_sale_publishes_its_sign_ins_and_stocks_up_only_once()
    {
        using var client = _demo.CreateClient();
        var events = await WaitForEventsAsync(client);

        var seeder = _demo.Services.GetServices<IHostedService>().OfType<DemoSeeder>().Single();
        using var http = _demo.CreateClient();
        await seeder.SeedAsync(http, CancellationToken.None);
        var again = await client.GetFromJsonAsync<PageDto>("/api/events?pageSize=24");

        var accounts = await client.GetFromJsonAsync<List<DemoAccount>>("/api/demo");
        var signIns = new List<HttpStatusCode>();
        foreach (var account in accounts!)
        {
            signIns.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = account.Password })).StatusCode);
        }

        Assert.Equal(3, events);
        Assert.Equal(3, again!.Total);
        Assert.Equal(["Buyer", "Organizer"], accounts.Select(a => a.Role));
        Assert.All(signIns, status => Assert.Equal(HttpStatusCode.OK, status));
    }

    [Fact]
    public async Task Without_the_demo_there_are_no_published_sign_ins()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/demo")).StatusCode);
    }

    private static async Task<int> WaitForEventsAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var page = await client.GetFromJsonAsync<PageDto>("/api/events?pageSize=24");
            if (page!.Total >= 3)
            {
                return page.Total;
            }

            await Task.Delay(200);
        }

        return 0;
    }

    private sealed record PageDto(int Total);
}
