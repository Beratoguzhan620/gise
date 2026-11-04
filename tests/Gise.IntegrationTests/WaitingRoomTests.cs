using System.Net;
using System.Net.Http.Json;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class WaitingRoomTests(ApiFactory factory)
{
    [Fact]
    public async Task Early_arrivals_get_a_random_order_and_are_let_in_ten_a_minute()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, waitingRoom: true, saleStartsAt: now.AddMinutes(10)));
        var buyers = new List<Person>();
        for (var i = 0; i < 25; i++)
        {
            buyers.Add(await factory.RegisterAsync($"Sıradaki {i}"));
        }

        var joined = new List<QueueDto>();
        foreach (var buyer in buyers)
        {
            joined.Add(await (await buyer.Http.PostAsync($"/api/events/{@event.Id}/queue", null)).ReadAsync<QueueDto>());
        }

        var positions = await PositionsAsync(buyers, @event.Id);
        Assert.Equal(Enumerable.Range(1, 25).Select(i => (long)i), positions.Order());
        Assert.NotEqual(Enumerable.Range(1, 25).Select(i => (long)i), positions);
        Assert.All(joined, j => Assert.False(j.Admitted));

        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(10, await AdmittedAsync(buyers, @event.Id));
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(20, await AdmittedAsync(buyers, @event.Id));
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(25, await AdmittedAsync(buyers, @event.Id));
        foreach (var buyer in buyers)
        {
            buyer.Dispose();
        }
    }

    [Fact]
    public async Task Places_are_only_for_people_whose_turn_it_is()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, waitingRoom: true));
        using var skipper = await factory.RegisterAsync("Sırayı atlayan");

        var withoutJoining = await skipper.HoldAsync(@event.Id, Buying.Standing(1));
        var status = await (await skipper.Http.PostAsync($"/api/events/{@event.Id}/queue", null)).ReadAsync<QueueDto>();
        var afterJoining = await skipper.HoldAsync(@event.Id, Buying.Standing(1));

        Assert.Equal(HttpStatusCode.Conflict, withoutJoining.StatusCode);
        Assert.Equal("queue_required", await withoutJoining.ProblemCodeAsync());
        Assert.True(status.Admitted);
        Assert.Equal(HttpStatusCode.Created, afterJoining.StatusCode);
    }

    [Fact]
    public async Task Late_arrivals_queue_in_order_and_hear_how_long_to_wait()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, waitingRoom: true, saleStartsAt: now));
        var statuses = new List<QueueDto>();
        for (var i = 0; i < 12; i++)
        {
            using var buyer = await factory.RegisterAsync($"Geç gelen {i}");
            statuses.Add(await (await buyer.Http.PostAsync($"/api/events/{@event.Id}/queue", null)).ReadAsync<QueueDto>());
        }

        Assert.Equal(Enumerable.Range(1, 12).Select(i => (long)i), statuses.Select(s => s.Position!.Value));
        Assert.True(statuses[9].Admitted);
        Assert.False(statuses[10].Admitted);
        Assert.InRange(statuses[11].EstimatedWaitSeconds!.Value, 1, 60);
    }

    [Fact]
    public async Task When_shopping_time_runs_out_the_buyer_goes_to_the_back()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, waitingRoom: true));
        using var dawdler = await factory.RegisterAsync("Oyalanan");
        using var newcomer = await factory.RegisterAsync("Yeni gelen");
        Assert.True((await (await dawdler.Http.PostAsync($"/api/events/{@event.Id}/queue", null)).ReadAsync<QueueDto>()).Admitted);

        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        var expired = await dawdler.HoldAsync(@event.Id, Buying.Standing(1));
        await newcomer.Http.PostAsync($"/api/events/{@event.Id}/queue", null);
        var rejoined = await (await dawdler.Http.PostAsync($"/api/events/{@event.Id}/queue", null)).ReadAsync<QueueDto>();

        Assert.Equal("queue_required", await expired.ProblemCodeAsync());
        Assert.Contains("ran out", await expired.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(2, rejoined.Position);
        Assert.True(rejoined.Admitted);
    }

    [Fact]
    public async Task Events_without_a_waiting_room_let_everyone_in()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();

        var status = await (await buyer.Http.PostAsync($"/api/events/{@event.Id}/queue", null)).ReadAsync<QueueDto>();

        Assert.Equal((false, true, (long?)null), (status.WaitingRoom, status.Admitted, status.Position));
    }

    private static async Task<List<long>> PositionsAsync(List<Person> buyers, Guid eventId)
    {
        var positions = new List<long>();
        foreach (var buyer in buyers)
        {
            positions.Add((await (await buyer.Http.GetAsync($"/api/events/{eventId}/queue")).ReadAsync<QueueDto>()).Position!.Value);
        }

        return positions;
    }

    private static async Task<int> AdmittedAsync(List<Person> buyers, Guid eventId)
    {
        var admitted = 0;
        foreach (var buyer in buyers)
        {
            if ((await (await buyer.Http.GetAsync($"/api/events/{eventId}/queue")).ReadAsync<QueueDto>()).Admitted)
            {
                admitted++;
            }
        }

        return admitted;
    }
}
