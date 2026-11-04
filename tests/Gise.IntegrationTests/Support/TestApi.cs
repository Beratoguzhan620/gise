using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gise.IntegrationTests.Support;

/// <summary>A signed-in person with an HTTP client that carries their token.</summary>
public sealed class Person(ApiFactory factory, SessionDto session) : IDisposable
{
    public HttpClient Http { get; } = Client(factory, session.AccessToken);

    public Guid Id => session.User.Id;

    public string Email => session.User.Email;

    public string Name => session.User.DisplayName;

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

/// <summary>An organizer with a manager-owner, a small hall and helpers to put events on sale.</summary>
public sealed record Organizer(Person Owner, Guid Id, Guid VenueId);

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    /// <summary>Stalls A (3 rows × 10), balcony B (2 rows × 8) and a standing floor for 40: 86 places.</summary>
    public const int HallCapacity = 86;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static object Hall(string name = "Kültür Merkezi") => new
    {
        name,
        city = "Samsun",
        address = "Atatürk Bulvarı 1",
        sections = new object[]
        {
            new { key = "A", name = "Parter", kind = "Seated", rows = new[] { new { label = "A", seats = 10 }, new { label = "B", seats = 10 }, new { label = "C", seats = 10 } } },
            new { key = "B", name = "Balkon", kind = "Seated", rows = new[] { new { label = "A", seats = 8 }, new { label = "B", seats = 8 } } },
            new { key = "floor", name = "Ayakta", kind = "Standing", capacity = 40 },
        },
    };

    public static object EventBody(Guid venueId, DateTimeOffset now, string title = "Akustik Gece", bool waitingRoom = false, int? refundCutoffHours = 48,
        DateTimeOffset? saleStartsAt = null, int maxTickets = 6) => new
    {
        venueId,
        title,
        description = "Bir akşam, üç sahne.",
        startsAt = now.AddDays(30),
        saleStartsAt = saleStartsAt ?? now.AddMinutes(-1),
        maxTicketsPerBuyer = maxTickets,
        refundCutoffHours,
        waitingRoom,
        admitPerMinute = waitingRoom ? 10 : 0,
        categories = new[] { new { name = "Ön", price = 750.00m }, new { name = "Balkon", price = 450.50m }, new { name = "Ayakta", price = 300m } },
        pricing = new[] { new { section = "A", category = "Ön" }, new { section = "B", category = "Balkon" }, new { section = "floor", category = "Ayakta" } },
    };

    public static async Task<Person> RegisterAsync(this ApiFactory factory, string name = "Ayşe Yılmaz")
    {
        using var anonymous = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password = Password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    public static async Task<Organizer> OrganizerAsync(this ApiFactory factory, string name = "Karadeniz Sahne")
    {
        var owner = await factory.RegisterAsync("Deniz Organizatör");
        var organization = await (await owner.Http.PostAsJsonAsync("/api/organizations", new { name })).ReadAsync<OrganizationDto>();
        var venue = await owner.Http.PostAsJsonAsync($"/api/organizations/{organization.Id}/venues", Hall());
        Assert.True(venue.IsSuccessStatusCode, await venue.Content.ReadAsStringAsync());
        return new Organizer(owner, organization.Id, (await venue.ReadAsync<VenueDto>()).Id);
    }

    public static async Task<OrganizerEventDto> DraftAsync(this Organizer organizer, ApiFactory factory, object? body = null)
    {
        var response = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/events",
            body ?? EventBody(organizer.VenueId, factory.Clock.GetUtcNow()));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrganizerEventDto>();
    }

    public static async Task<OrganizerEventDto> PublishedAsync(this Organizer organizer, ApiFactory factory, object? body = null)
    {
        var draft = await organizer.DraftAsync(factory, body);
        var response = await organizer.Owner.Http.PostAsync($"/api/organizations/{organizer.Id}/events/{draft.Id}/publish", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrganizerEventDto>();
    }

    public static async Task AddMemberAsync(this Organizer organizer, Person member, string role)
    {
        var response = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/members", new { email = member.Email, role });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
