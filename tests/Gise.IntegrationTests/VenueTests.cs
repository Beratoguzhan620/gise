using System.Net;
using System.Net.Http.Json;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class VenueTests(ApiFactory factory)
{
    [Fact]
    public async Task A_venue_keeps_its_floor_plan_and_counts_its_places()
    {
        var organizer = await factory.OrganizerAsync();

        var venue = await (await organizer.Owner.Http.GetAsync($"/api/organizations/{organizer.Id}/venues/{organizer.VenueId}")).ReadAsync<VenueDto>();

        Assert.Equal(TestApi.HallCapacity, venue.Capacity);
        Assert.Equal(["A", "B", "floor"], venue.Sections.Select(s => s.Key));
        Assert.Equal([30, 16, 40], venue.Sections.Select(s => s.Places));
        Assert.Equal(["A", "B", "C"], venue.Sections[0].Rows.Select(r => r.Label));
    }

    [Theory]
    [InlineData("""[{"key":"A","name":"Parter","kind":"Seated","rows":[{"label":"A","seats":10}]},{"key":"a","name":"Tekrar","kind":"Standing","capacity":10}]""", "each key is used once")]
    [InlineData("""[{"key":"A","name":"Parter","kind":"Seated","rows":[{"label":"A","seats":81}]}]""", "1 to 80 seats")]
    [InlineData("""[{"key":"A","name":"Parter","kind":"Seated","rows":[{"label":"A","seats":5},{"label":"A","seats":5}]}]""", "row label twice")]
    [InlineData("""[{"key":"floor","name":"Ayakta","kind":"Standing","capacity":0}]""", "holds 1 to")]
    [InlineData("""[{"key":"A B","name":"Parter","kind":"Standing","capacity":10}]""", "letters, digits")]
    [InlineData("""[]""", "1 to 24 sections")]
    public async Task Floor_plans_that_cannot_be_sold_are_refused_with_a_reason(string sections, string reason)
    {
        var organizer = await factory.OrganizerAsync();
        var body = $$"""{"name":"Salon","city":"Samsun","sections":{{sections}}}""";

        var response = await organizer.Owner.Http.PostAsync($"/api/organizations/{organizer.Id}/venues",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stadium_sized_plan_fits_and_a_bigger_one_does_not()
    {
        var organizer = await factory.OrganizerAsync();
        object Plan(int areas) => new
        {
            name = "Stadyum",
            city = "Samsun",
            sections = Enumerable.Range(1, areas).Select(i => new { key = $"S{i}", name = $"Tribün {i}", kind = "Standing", capacity = 20_000 }).ToArray(),
        };

        var fits = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/venues", new
        {
            name = "Stadyum",
            city = "Samsun",
            sections = new object[] { new { key = "N", name = "Kuzey", kind = "Standing", capacity = 20_000 }, new { key = "S", name = "Güney", kind = "Standing", capacity = 20_000 }, new { key = "E", name = "Doğu", kind = "Standing", capacity = 10_000 } },
        });
        var tooBig = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/venues", Plan(3));

        Assert.Equal(HttpStatusCode.Created, fits.StatusCode);
        Assert.Equal(50_000, (await fits.ReadAsync<VenueDto>()).Capacity);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
    }

    [Fact]
    public async Task A_venue_in_use_cannot_be_deleted()
    {
        var organizer = await factory.OrganizerAsync();
        await organizer.DraftAsync(factory);

        var response = await organizer.Owner.Http.DeleteAsync($"/api/organizations/{organizer.Id}/venues/{organizer.VenueId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Another_organizers_venue_is_out_of_reach()
    {
        var mine = await factory.OrganizerAsync();
        var theirs = await factory.OrganizerAsync("Rakip");

        var response = await mine.Owner.Http.PostAsJsonAsync($"/api/organizations/{mine.Id}/events", TestApi.EventBody(theirs.VenueId, factory.Clock.GetUtcNow()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
