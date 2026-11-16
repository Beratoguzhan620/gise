using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Demo;

/// <summary>
/// Keeps the public demo stocked: an organizer with two venues and a few events on sale. It goes through the API's own
/// endpoints, so the sample data passes the same rules as anything a visitor creates. When visitors have cancelled
/// events or the dates have passed, the next start adds a fresh set.
/// </summary>
internal sealed partial class DemoSeeder(
    IOptions<DemoOptions> options, IHttpClientFactory clients, IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<DemoSeeder> logger)
    : BackgroundService
{
    public const string HttpClientName = "Gise.Demo";

    private const int EnoughEvents = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
        {
            return;
        }

        try
        {
            using var http = clients.CreateClient(HttpClientName);
            await SeedAsync(http, stoppingToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            // The demo still works without sample data; visitors can create their own.
            LogSeedFailed(logger, ex);
        }
    }

    private async Task<bool> StartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
        return started.Task.IsCompleted && !ct.IsCancellationRequested;
    }

    /// <summary>Runs once; public so tests can run it again and see that a stocked demo is left alone.</summary>
    public async Task SeedAsync(HttpClient http, CancellationToken ct)
    {
        var demo = options.Value;
        var onSale = await http.GetFromJsonAsync<Page>("/api/events?pageSize=24", ct);
        if (onSale is { Total: >= EnoughEvents })
        {
            return;
        }

        var organizer = await SignInAsync(http, demo.OrganizerEmail, "Deniz Yılmaz", demo.Password, ct);
        await SignInAsync(http, demo.BuyerEmail, "Ayşe Demir", demo.Password, ct);

        var me = await SendAsync<Me>(http, HttpMethod.Get, "/api/me", organizer, null, ct);
        var organization = me.Organizations.FirstOrDefault()?.Id
            ?? (await SendAsync<Created>(http, HttpMethod.Post, "/api/organizations", organizer, new { name = "Karadeniz Sahne" }, ct)).Id;
        var o = $"/api/organizations/{organization}";

        var venues = await SendAsync<List<Named>>(http, HttpMethod.Get, $"{o}/venues", organizer, null, ct);
        var hall = venues.FirstOrDefault(v => v.Name == "Atatürk Kültür Merkezi")?.Id
            ?? (await SendAsync<Created>(http, HttpMethod.Post, $"{o}/venues", organizer, new
            {
                name = "Atatürk Kültür Merkezi",
                city = "Samsun",
                address = "Cumhuriyet Mah., Atakum",
                sections = new object[]
                {
                    new { key = "A", name = "Parter", kind = "Seated", rows = Rows(9, 22) },
                    new { key = "B", name = "Balkon", kind = "Seated", rows = Rows(4, 18) },
                    new { key = "floor", name = "Ayakta", kind = "Standing", capacity = 120 },
                },
            }, ct)).Id;
        var club = venues.FirstOrDefault(v => v.Name == "Liman Sahnesi")?.Id
            ?? (await SendAsync<Created>(http, HttpMethod.Post, $"{o}/venues", organizer, new
            {
                name = "Liman Sahnesi",
                city = "Trabzon",
                address = "Kemerkaya Mah.",
                sections = new object[]
                {
                    new { key = "floor", name = "Ayakta", kind = "Standing", capacity = 400 },
                    new { key = "L", name = "Loca", kind = "Seated", rows = Rows(2, 12) },
                },
            }, ct)).Id;

        var three = new object[] { new { name = "Parter", price = 650 }, new { name = "Balkon", price = 420 }, new { name = "Ayakta", price = 300 } };
        var threePricing = new object[]
        {
            new { section = "A", category = "Parter" }, new { section = "B", category = "Balkon" }, new { section = "floor", category = "Ayakta" },
        };
        var events = new object[]
        {
            new
            {
                venueId = hall, title = "Karadeniz Ezgileri: Kemençe Gecesi", startsAt = Day(17, 20), saleStartsAt = Day(-1, 10),
                description = "Kemençe, tulum ve kaval ile iki perdelik bir akşam. Ara 20 dakika.\n\nKapılar 19.00'da açılır.",
                maxTicketsPerBuyer = 6, refundCutoffHours = 48, categories = three, pricing = threePricing,
            },
            new
            {
                venueId = club, title = "Fırtına Vadisi Caz Gecesi", startsAt = Day(24, 21), saleStartsAt = Day(-1, 10),
                maxTicketsPerBuyer = 6, refundCutoffHours = 48,
                categories = new object[] { new { name = "Ayakta", price = 380 }, new { name = "Loca", price = 950 } },
                pricing = new object[] { new { section = "floor", category = "Ayakta" }, new { section = "L", category = "Loca" } },
            },
            new
            {
                venueId = hall, title = "Hamlet, iki perde", startsAt = Day(31, 20), saleStartsAt = Day(-1, 10),
                maxTicketsPerBuyer = 6, refundCutoffHours = 48, categories = three, pricing = threePricing,
            },
        };

        foreach (var body in events)
        {
            var created = await SendAsync<Created>(http, HttpMethod.Post, $"{o}/events", organizer, body, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"{o}/events/{created.Id}/publish", organizer, new { }, ct);
        }

        LogSeeded(logger, events.Length);
    }

    private static object[] Rows(int count, int seats) =>
        Enumerable.Range(0, count).Select(i => (object)new { label = "ABCDEFGHIJKLMNOP"[i].ToString(), seats }).ToArray();

    /// <summary>A day from now at a local (Istanbul) hour, as the organizer would type it.</summary>
    private DateTimeOffset Day(int days, int hour)
    {
        var today = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3)).Date;
        return new DateTimeOffset(today.AddDays(days).AddHours(hour), TimeSpan.FromHours(3));
    }

    private static async Task<string> SignInAsync(HttpClient http, string email, string name, string password, CancellationToken ct)
    {
        using var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
        if (login.IsSuccessStatusCode)
        {
            return (await login.Content.ReadFromJsonAsync<Session>(ct))!.AccessToken;
        }

        using var register = await http.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password }, ct);
        if (!register.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The demo account {email} could not be created: {(int)register.StatusCode}.");
        }

        return (await register.Content.ReadFromJsonAsync<Session>(ct))!.AccessToken;
    }

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }

        return response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
            ? default!
            : (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private sealed record Page(int Total);

    private sealed record Session(string AccessToken);

    private sealed record Created(Guid Id);

    private sealed record Named(Guid Id, string Name);

    private sealed record Me(List<Created> Organizations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added {Count} demo events")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not add the demo events")]
    private static partial void LogSeedFailed(ILogger logger, Exception exception);
}
