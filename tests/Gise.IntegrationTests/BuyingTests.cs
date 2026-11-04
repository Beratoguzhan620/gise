using System.Net;
using System.Net.Http.Json;
using Gise.Infrastructure.Payments;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BuyingTests(ApiFactory factory)
{
    [Fact]
    public async Task A_buyer_holds_pays_and_the_seats_are_sold()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync("Elif Alıcı");

        var hold = await buyer.HeldAsync(@event.Id, new
        {
            seats = new[] { new { section = "A", row = "B", number = 5 }, new { section = "A", row = "B", number = 6 } },
            standing = new[] { new { section = "floor", quantity = 1 } },
        });
        var order = await buyer.CheckoutAsync(hold.Id);

        Assert.Equal(("Active", 1800.00m, 3), (hold.Status, hold.Total, hold.Lines.Count));
        Assert.Equal(["Parter B5", "Parter B6", "Ayakta"], hold.Lines.Select(l => l.Row is null ? l.SectionName : $"{l.SectionName} {l.Row}{l.Number}"));
        Assert.Equal(("PendingPayment", 1800.00m), (order.Status, order.Amount));
        Assert.StartsWith("GS-", order.Number, StringComparison.Ordinal);
        Assert.StartsWith("https://sandbox.test/checkout/", order.CheckoutUrl, StringComparison.Ordinal);
        Assert.Equal(180_000, factory.Payments.Requests.Single(r => r.OrderId == order.Id).AmountMinor);

        (await factory.NotifyAsync(order, PaymentState.Succeeded)).EnsureSuccessStatusCode();

        var paid = await buyer.OrderAsync(order.Id);
        Assert.Equal("Paid", paid.Status);
        Assert.Null(paid.CheckoutUrl);
        Assert.True(paid.CanRefund);
        Assert.Equal(3, await factory.CountPlacesAsync(@event.Id, "Sold"));
        using var anonymous = factory.CreateClient();
        var availability = await (await anonymous.GetAsync($"/api/events/{@event.Id}/availability")).ReadAsync<AvailabilityDto>();
        Assert.Equal("1111001111", availability.Sections[0].Rows[1].Seats);
        Assert.Equal(TestApi.HallCapacity - 3, availability.Available);
        var mine = await (await buyer.Http.GetAsync("/api/orders")).ReadAsync<PageDto<OrderSummaryDto>>();
        Assert.Equal((order.Number, 3), (mine.Items.Single().Number, mine.Items.Single().Tickets));
    }

    [Fact]
    public async Task Either_every_seat_is_taken_or_none_is()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var first = await factory.RegisterAsync("Birinci");
        using var second = await factory.RegisterAsync("İkinci");
        await first.HeldAsync(@event.Id, Buying.Seats("A-A-1"));

        var response = await second.HoldAsync(@event.Id, Buying.Seats("A-A-2", "A-A-1"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("place_unavailable", await response.ProblemCodeAsync());
        Assert.Contains("Parter A1", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await factory.CountPlacesAsync(@event.Id, "Held"));
        await second.HeldAsync(@event.Id, Buying.Seats("A-A-2"));
    }

    [Fact]
    public async Task Twenty_buyers_on_one_seat_and_sixty_on_forty_standing_spots_never_oversell()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        var buyers = await Task.WhenAll(Enumerable.Range(0, 80).Select(i => factory.RegisterAsync($"Alıcı {i}")));

        var seatRace = Task.WhenAll(buyers[60..].Select(b => b.HoldAsync(@event.Id, Buying.Seats("A-C-7"))));
        var standingRace = Task.WhenAll(buyers[..60].Select(b => b.HoldAsync(@event.Id, Buying.Standing(1))));
        var seat = await seatRace;
        var standing = await standingRace;

        Assert.Single(seat, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(19, seat.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(40, standing.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(20, standing.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(41, await factory.CountPlacesAsync(@event.Id, "Held"));
        Assert.Equal(40, await factory.ScalarAsync<long>("SELECT count(DISTINCT hold_id) FROM places WHERE event_id = @p0 AND section_key = 'floor'", @event.Id));
        foreach (var buyer in buyers)
        {
            buyer.Dispose();
        }
    }

    [Fact]
    public async Task Held_places_come_back_when_time_runs_out()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var slow = await factory.RegisterAsync("Yavaş");
        using var other = await factory.RegisterAsync("Sonraki");
        var hold = await slow.HeldAsync(@event.Id, Buying.Seats("B-A-1"));

        Assert.Equal(HttpStatusCode.Conflict, (await other.HoldAsync(@event.Id, Buying.Seats("B-A-1"))).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(9));

        await other.HeldAsync(@event.Id, Buying.Seats("B-A-1"));
        var late = await slow.Http.PostAsync($"/api/holds/{hold.Id}/checkout", null);
        Assert.Equal(HttpStatusCode.Gone, late.StatusCode);
        Assert.Equal("hold_expired", await late.ProblemCodeAsync());
        Assert.Equal("Expired", (await (await slow.Http.GetAsync($"/api/holds/{hold.Id}")).ReadAsync<HoldDto>()).Status);
    }

    [Fact]
    public async Task Choosing_again_replaces_the_basket_and_letting_go_frees_the_seats()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();

        var first = await buyer.HeldAsync(@event.Id, Buying.Seats("A-A-1", "A-A-2"));
        var second = await buyer.HeldAsync(@event.Id, Buying.Seats("A-A-9"));

        Assert.Equal("Released", (await (await buyer.Http.GetAsync($"/api/holds/{first.Id}")).ReadAsync<HoldDto>()).Status);
        Assert.Equal(1, await factory.CountPlacesAsync(@event.Id, "Held"));
        (await buyer.Http.DeleteAsync($"/api/holds/{second.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Held"));
    }

    [Fact]
    public async Task One_buyer_gets_at_most_the_events_ticket_limit()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), maxTickets: 4));
        using var buyer = await factory.RegisterAsync();

        var five = await buyer.HoldAsync(@event.Id, Buying.Standing(5));
        await factory.BuyAsync(buyer, @event.Id, Buying.Standing(3));
        var twoMore = await buyer.HoldAsync(@event.Id, Buying.Standing(2));
        var oneMore = await buyer.HoldAsync(@event.Id, Buying.Standing(1));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, five.StatusCode);
        Assert.Equal("ticket_limit_reached", await twoMore.ProblemCodeAsync());
        Assert.Contains("you already have 3", await twoMore.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, oneMore.StatusCode);
    }

    [Fact]
    public async Task Nothing_sells_before_the_sale_opens()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, saleStartsAt: now.AddHours(1)));
        using var buyer = await factory.RegisterAsync();

        var response = await buyer.HoldAsync(@event.Id, Buying.Standing(1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sale_not_open", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("""{"seats":[{"section":"A","row":"Z","number":1}]}""", "has no seat 1 in row Z")]
    [InlineData("""{"seats":[{"section":"floor","row":"A","number":1}]}""", "no seated section")]
    [InlineData("""{"standing":[{"section":"A","quantity":1}]}""", "no standing area")]
    [InlineData("""{"seats":[{"section":"A","row":"A","number":1},{"section":"A","row":"A","number":1}]}""", "twice")]
    [InlineData("""{}""", "at least one place")]
    public async Task Requests_for_places_that_do_not_exist_are_refused(string body, string reason)
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();

        var response = await buyer.Http.PostAsync($"/api/events/{@event.Id}/holds", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_out_twice_gives_the_same_order()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var hold = await buyer.HeldAsync(@event.Id, Buying.Standing(2));

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => buyer.Http.PostAsync($"/api/holds/{hold.Id}/checkout", null)));

        var orders = await Task.WhenAll(responses.Select(r => r.ReadAsync<OrderDto>()));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(orders.Select(o => o.Id).Distinct());
        Assert.Single(factory.Payments.Requests, r => r.OrderId == orders[0].Id);
    }

    [Fact]
    public async Task When_the_provider_is_down_nothing_is_charged_and_the_places_go_back()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var hold = await buyer.HeldAsync(@event.Id, Buying.Seats("B-B-8"));

        factory.Payments.Down = true;
        HttpResponseMessage response;
        try
        {
            response = await buyer.Http.PostAsync($"/api/holds/{hold.Id}/checkout", null);
        }
        finally
        {
            factory.Payments.Down = false;
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("payment_unavailable", await response.ProblemCodeAsync());
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Held"));
    }

    [Fact]
    public async Task A_declined_payment_puts_the_places_back_on_sale()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await buyer.CheckoutAsync((await buyer.HeldAsync(@event.Id, Buying.Standing(2))).Id);

        (await factory.NotifyAsync(order, PaymentState.Failed)).EnsureSuccessStatusCode();

        Assert.Equal("Failed", (await buyer.OrderAsync(order.Id)).Status);
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Held"));
    }

    [Fact]
    public async Task Orders_are_private_to_their_buyer()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        using var stranger = await factory.RegisterAsync("Meraklı");
        var hold = await buyer.HeldAsync(@event.Id, Buying.Standing(1));
        var order = await buyer.CheckoutAsync(hold.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/holds/{hold.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.PostAsync($"/api/holds/{hold.Id}/checkout", null)).StatusCode);
    }
}
