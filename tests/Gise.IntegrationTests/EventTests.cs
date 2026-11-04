using System.Net;
using System.Net.Http.Json;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class EventTests(ApiFactory factory)
{
    [Fact]
    public async Task A_draft_is_private_until_it_is_published()
    {
        var organizer = await factory.OrganizerAsync();
        using var anonymous = factory.CreateClient();
        var draft = await organizer.DraftAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: $"Gizli {Guid.NewGuid():N}"));

        Assert.Equal("Draft", draft.Status);
        Assert.Equal(TestApi.HallCapacity, draft.Inventory.Available);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{draft.Id}")).StatusCode);
        Assert.Equal(0, (await (await anonymous.GetAsync($"/api/events?q={draft.Title}")).ReadAsync<PageDto<EventSummaryDto>>()).Total);
    }

    [Fact]
    public async Task Publishing_creates_a_place_for_every_seat_and_standing_spot()
    {
        var organizer = await factory.OrganizerAsync();

        var published = await organizer.PublishedAsync(factory);

        Assert.Equal(("Published", "OnSale"), (published.Status, published.SaleState));
        Assert.Equal(TestApi.HallCapacity, await factory.ScalarAsync<long>("SELECT count(*) FROM places WHERE event_id = @p0", published.Id));
        Assert.Equal(30, await factory.ScalarAsync<long>("SELECT count(*) FROM places WHERE event_id = @p0 AND section_key = 'A'", published.Id));
        Assert.Equal(["A", "B", "floor"], published.Layout.Select(s => s.Key));
    }

    [Fact]
    public async Task Buyers_see_the_event_its_prices_and_every_free_seat()
    {
        var organizer = await factory.OrganizerAsync();
        var published = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: $"Konser {Guid.NewGuid():N}"));
        using var anonymous = factory.CreateClient();

        var list = await (await anonymous.GetAsync($"/api/events?q={published.Title}")).ReadAsync<PageDto<EventSummaryDto>>();
        var detail = await (await anonymous.GetAsync($"/api/events/{published.Id}")).ReadAsync<EventDetailDto>();
        var availability = await (await anonymous.GetAsync($"/api/events/{published.Id}/availability")).ReadAsync<AvailabilityDto>();

        var summary = Assert.Single(list.Items);
        Assert.Equal((300m, "TRY", "OnSale", "Karadeniz Sahne"), (summary.PriceFrom, summary.Currency, summary.SaleState, summary.Organizer));
        Assert.Equal([750.00m, 450.50m, 300m], detail.Categories.Select(c => c.Price));
        Assert.Equal(detail.Categories[1].Id, detail.Sections.Single(s => s.Key == "B").CategoryId);
        Assert.Equal(published.StartsAt.AddHours(-48), detail.RefundDeadline);
        Assert.Equal(TestApi.HallCapacity, availability.Available);
        Assert.Equal("1111111111", availability.Sections[0].Rows[0].Seats);
        Assert.Equal((40, 0), (availability.Sections[2].Available, availability.Sections[2].Rows.Count));
    }

    [Fact]
    public async Task Before_the_sale_starts_the_event_shows_as_upcoming()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();

        var published = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, saleStartsAt: now.AddDays(2)));

        Assert.Equal("Upcoming", published.SaleState);
    }

    [Theory]
    [InlineData("missing-section", "Every section needs a price")]
    [InlineData("unknown-category", "names a category that does not exist")]
    [InlineData("sale-after-start", "Sales start before the event does")]
    [InlineData("past", "The event starts in the future")]
    [InlineData("cents", "to the kuruş")]
    public async Task Events_that_cannot_sell_properly_are_refused(string problem, string reason)
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        object Body(DateTimeOffset startsAt, DateTimeOffset saleStartsAt, decimal price, object[] pricing) => new
        {
            venueId = organizer.VenueId,
            title = "Hatalı",
            startsAt,
            saleStartsAt,
            categories = new[] { new { name = "Tek", price } },
            pricing,
        };
        object[] all = [new { section = "A", category = "Tek" }, new { section = "B", category = "Tek" }, new { section = "floor", category = "Tek" }];
        var body = problem switch
        {
            "missing-section" => Body(now.AddDays(5), now, 100m, all[..2]),
            "unknown-category" => Body(now.AddDays(5), now, 100m, [.. all[..2], new { section = "floor", category = "VIP" }]),
            "sale-after-start" => Body(now.AddDays(5), now.AddDays(6), 100m, all),
            "past" => Body(now.AddDays(-1), now.AddDays(-2), 100m, all),
            _ => Body(now.AddDays(5), now, 100.555m, all),
        };

        var response = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/events", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_drafts_change_and_an_event_is_published_once()
    {
        var organizer = await factory.OrganizerAsync();
        var draft = await organizer.DraftAsync(factory);
        var path = $"/api/organizations/{organizer.Id}/events/{draft.Id}";

        var renamed = await organizer.Owner.Http.PutAsJsonAsync(path, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: "Yeni ad"));
        (await organizer.Owner.Http.PostAsync($"{path}/publish", null)).EnsureSuccessStatusCode();
        var again = await organizer.Owner.Http.PostAsync($"{path}/publish", null);
        var change = await organizer.Owner.Http.PutAsJsonAsync(path, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow()));
        var delete = await organizer.Owner.Http.DeleteAsync(path);

        Assert.Equal("Yeni ad", (await renamed.ReadAsync<OrganizerEventDto>()).Title);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Two_managers_publishing_at_once_create_the_places_once()
    {
        var organizer = await factory.OrganizerAsync();
        var draft = await organizer.DraftAsync(factory);
        var path = $"/api/organizations/{organizer.Id}/events/{draft.Id}/publish";

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => organizer.Owner.Http.PostAsync(path, null)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(TestApi.HallCapacity, await factory.ScalarAsync<long>("SELECT count(*) FROM places WHERE event_id = @p0", draft.Id));
    }

    [Fact]
    public async Task Changing_the_venue_later_leaves_events_on_sale_as_they_were()
    {
        var organizer = await factory.OrganizerAsync();
        var published = await organizer.PublishedAsync(factory);
        var smaller = new
        {
            name = "Kültür Merkezi",
            city = "Samsun",
            sections = new object[] { new { key = "A", name = "Parter", kind = "Seated", rows = new[] { new { label = "A", seats = 5 } } } },
        };

        (await organizer.Owner.Http.PutAsJsonAsync($"/api/organizations/{organizer.Id}/venues/{organizer.VenueId}", smaller)).EnsureSuccessStatusCode();
        using var anonymous = factory.CreateClient();
        var detail = await (await anonymous.GetAsync($"/api/events/{published.Id}")).ReadAsync<EventDetailDto>();

        Assert.Equal(TestApi.HallCapacity, detail.Capacity);
        Assert.Equal(3, detail.Sections.Count);
    }

    [Fact]
    public async Task A_draft_priced_for_an_old_floor_plan_must_be_repriced_before_publishing()
    {
        var organizer = await factory.OrganizerAsync();
        var draft = await organizer.DraftAsync(factory);
        var plan = new
        {
            name = "Kültür Merkezi",
            city = "Samsun",
            sections = new object[] { new { key = "A", name = "Parter", kind = "Seated", rows = new[] { new { label = "A", seats = 5 } } }, new { key = "VIP", name = "Loca", kind = "Standing", capacity = 6 } },
        };
        (await organizer.Owner.Http.PutAsJsonAsync($"/api/organizations/{organizer.Id}/venues/{organizer.VenueId}", plan)).EnsureSuccessStatusCode();

        var response = await organizer.Owner.Http.PostAsync($"/api/organizations/{organizer.Id}/events/{draft.Id}/publish", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VIP", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_catalog_searches_titles_venues_and_cities()
    {
        var organizer = await factory.OrganizerAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: $"Caz {tag}"));
        await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: $"Rock {tag}"));
        using var anonymous = factory.CreateClient();

        var both = await (await anonymous.GetAsync($"/api/events?q={tag}&city=samsun")).ReadAsync<PageDto<EventSummaryDto>>();
        var one = await (await anonymous.GetAsync($"/api/events?q=caz {tag}")).ReadAsync<PageDto<EventSummaryDto>>();
        var elsewhere = await (await anonymous.GetAsync($"/api/events?q={tag}&city=Ankara")).ReadAsync<PageDto<EventSummaryDto>>();

        Assert.Equal(2, both.Total);
        Assert.Equal($"Caz {tag}", Assert.Single(one.Items).Title);
        Assert.Equal(0, elsewhere.Total);
    }
}
