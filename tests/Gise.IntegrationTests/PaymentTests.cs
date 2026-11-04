using System.Net;
using System.Net.Http.Json;
using Gise.Infrastructure.Payments;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentTests(ApiFactory factory)
{
    [Fact]
    public async Task Notices_with_a_wrong_or_stale_signature_change_nothing()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await buyer.CheckoutAsync((await buyer.HeldAsync(@event.Id, Buying.Standing(1))).Id);

        var forged = await factory.NotifyAsync(order, PaymentState.Succeeded, secret: "guessed-secret");
        var replayed = await factory.NotifyAsync(order, PaymentState.Succeeded, signedAt: factory.Clock.GetUtcNow().AddMinutes(-10));
        var unsigned = await factory.SendNoticeAsync("{}", "");

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal("invalid_signature", await forged.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
        Assert.Equal("PendingPayment", (await buyer.OrderAsync(order.Id)).Status);
    }

    [Fact]
    public async Task A_notice_delivered_twice_is_acted_on_once()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await buyer.CheckoutAsync((await buyer.HeldAsync(@event.Id, Buying.Standing(2))).Id);

        var first = await (await factory.NotifyAsync(order, PaymentState.Succeeded, noticeId: $"evt_{order.Id:N}")).ReadAsync<WebhookDto>();
        var again = await (await factory.NotifyAsync(order, PaymentState.Succeeded, noticeId: $"evt_{order.Id:N}")).ReadAsync<WebhookDto>();

        Assert.Equal(("Applied", "Duplicate"), (first.Outcome, again.Outcome));
        Assert.Equal(1, await factory.ScalarAsync<long>("SELECT count(*) FROM processed_notices WHERE order_id = @p0", order.Id));
        Assert.Equal(2, await factory.CountPlacesAsync(@event.Id, "Sold"));
    }

    [Fact]
    public async Task A_lost_webhook_is_made_up_for_by_asking_the_provider()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var payer = await factory.RegisterAsync("Ödeyen");
        using var quitter = await factory.RegisterAsync("Vazgeçen");
        var paid = await payer.CheckoutAsync((await payer.HeldAsync(@event.Id, Buying.Seats("A-A-3"))).Id);
        var abandoned = await quitter.CheckoutAsync((await quitter.HeldAsync(@event.Id, Buying.Seats("A-A-4"))).Id);
        factory.Payments.States[paid.CheckoutUrl!.Split('/').Last()] = PaymentState.Succeeded;

        await factory.ReconcileAsync();
        Assert.Equal("PendingPayment", (await payer.OrderAsync(paid.Id)).Status);

        factory.Clock.Advance(TimeSpan.FromMinutes(12));
        await factory.ReconcileAsync();

        Assert.Equal("Paid", (await payer.OrderAsync(paid.Id)).Status);
        Assert.Equal("Expired", (await quitter.OrderAsync(abandoned.Id)).Status);
        Assert.Equal(1, await factory.CountPlacesAsync(@event.Id, "Sold"));
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Held"));
    }

    [Fact]
    public async Task Money_that_arrives_after_the_seat_was_resold_goes_back()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var late = await factory.RegisterAsync("Geç ödeyen");
        using var next = await factory.RegisterAsync("Sonraki alıcı");
        var order = await late.CheckoutAsync((await late.HeldAsync(@event.Id, Buying.Seats("B-A-5"))).Id);
        factory.Clock.Advance(TimeSpan.FromMinutes(12));
        await factory.ReconcileAsync();
        await factory.BuyAsync(next, @event.Id, Buying.Seats("B-A-5"));

        (await factory.NotifyAsync(order, PaymentState.Succeeded)).EnsureSuccessStatusCode();

        var refunded = await late.OrderAsync(order.Id);
        Assert.Equal(("RefundPending", "LatePayment"), (refunded.Status, refunded.RefundReason));
        await factory.ReconcileAsync();
        Assert.Equal("Refunded", (await late.OrderAsync(order.Id)).Status);
        Assert.True(factory.Payments.Refunds.ContainsKey($"refund-{order.Id:N}"));
        Assert.Equal(1, await factory.CountPlacesAsync(@event.Id, "Sold"));
    }

    [Fact]
    public async Task Money_that_arrives_late_still_buys_the_seats_if_nobody_took_them()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var late = await factory.RegisterAsync("Geç ama şanslı");
        var order = await late.CheckoutAsync((await late.HeldAsync(@event.Id, Buying.Seats("B-B-1", "B-B-2"))).Id);
        factory.Clock.Advance(TimeSpan.FromMinutes(12));
        factory.Payments.Down = true;
        try
        {
            await factory.ReconcileAsync();
        }
        finally
        {
            factory.Payments.Down = false;
        }

        await factory.ReconcileAsync();
        Assert.Equal("Expired", (await late.OrderAsync(order.Id)).Status);

        (await factory.NotifyAsync(order, PaymentState.Succeeded)).EnsureSuccessStatusCode();

        Assert.Equal("Paid", (await late.OrderAsync(order.Id)).Status);
        Assert.Equal(2, await factory.CountPlacesAsync(@event.Id, "Sold"));
    }

    [Fact]
    public async Task Buyers_refund_until_the_deadline_and_the_seats_go_back_on_sale()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Seats("A-C-1", "A-C-2"));

        var refund = await buyer.Http.PostAsync($"/api/orders/{order.Id}/refund", null);

        var refunded = await refund.ReadAsync<OrderDto>();
        Assert.Equal(("Refunded", "Buyer", false), (refunded.Status, refunded.RefundReason, refunded.CanRefund));
        Assert.Equal(150_000, factory.Payments.Refunds[$"refund-{order.Id:N}"]);
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Sold"));
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.Http.PostAsync($"/api/orders/{order.Id}/refund", null)).StatusCode);
    }

    [Fact]
    public async Task After_the_deadline_or_when_the_organizer_decides_buyers_cannot_refund()
    {
        var organizer = await factory.OrganizerAsync();
        var now = factory.Clock.GetUtcNow();
        var soon = await organizer.PublishedAsync(factory, new
        {
            venueId = organizer.VenueId,
            title = "Yarın akşam",
            startsAt = now.AddHours(20),
            saleStartsAt = now.AddMinutes(-1),
            refundCutoffHours = 24,
            categories = new[] { new { name = "Tek", price = 100m } },
            pricing = new[] { new { section = "A", category = "Tek" }, new { section = "B", category = "Tek" }, new { section = "floor", category = "Tek" } },
        });
        var organizerOnly = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, now, refundCutoffHours: null));
        using var buyer = await factory.RegisterAsync();
        var first = await factory.BuyAsync(buyer, soon.Id, Buying.Standing(1));
        var second = await factory.BuyAsync(buyer, organizerOnly.Id, Buying.Standing(1));

        var closed = await buyer.Http.PostAsync($"/api/orders/{first.Id}/refund", null);
        var notOffered = await buyer.Http.PostAsync($"/api/orders/{second.Id}/refund", null);
        var byOrganizer = await organizer.Owner.Http.PostAsync($"/api/organizations/{organizer.Id}/events/{organizerOnly.Id}/orders/{second.Id}/refund", null);

        Assert.Equal("refund_closed", await closed.ProblemCodeAsync());
        Assert.Contains("organizer handles refunds", await notOffered.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Refunded", (await byOrganizer.ReadAsync<OrganizerOrderDto>()).Status);
    }

    [Fact]
    public async Task A_refund_the_provider_cannot_take_now_is_retried_later()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Standing(1));

        factory.Payments.Down = true;
        OrderDto waiting;
        try
        {
            waiting = await (await buyer.Http.PostAsync($"/api/orders/{order.Id}/refund", null)).ReadAsync<OrderDto>();
        }
        finally
        {
            factory.Payments.Down = false;
        }

        Assert.Equal("RefundPending", waiting.Status);
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Sold"));
        await factory.ReconcileAsync();
        Assert.Equal("Refunded", (await buyer.OrderAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Cancelling_an_event_stops_sales_and_refunds_everyone()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var early = await factory.RegisterAsync("Erken");
        using var paying = await factory.RegisterAsync("Ödemede");
        using var browsing = await factory.RegisterAsync("Bakınan");
        var paid = await factory.BuyAsync(early, @event.Id, Buying.Seats("A-A-5", "A-A-6"));
        var pending = await paying.CheckoutAsync((await paying.HeldAsync(@event.Id, Buying.Standing(3))).Id);
        await browsing.HeldAsync(@event.Id, Buying.Seats("B-B-3"));

        var cancelled = await (await organizer.Owner.Http.PostAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/cancel", null)).ReadAsync<CancelDto>();
        (await factory.NotifyAsync(pending, PaymentState.Succeeded)).EnsureSuccessStatusCode();
        await factory.ReconcileAsync();

        Assert.Equal((1, 1), (cancelled.OrdersToRefund, cancelled.Refunded));
        Assert.Equal(("Refunded", "EventCancelled"), ((await early.OrderAsync(paid.Id)).Status, (await early.OrderAsync(paid.Id)).RefundReason));
        Assert.Equal("Refunded", (await paying.OrderAsync(pending.Id)).Status);
        Assert.Equal("sale_not_open", await (await browsing.HoldAsync(@event.Id, Buying.Standing(1))).ProblemCodeAsync());
        using var anonymous = factory.CreateClient();
        Assert.Equal("Cancelled", (await (await anonymous.GetAsync($"/api/events/{@event.Id}")).ReadAsync<EventDetailDto>()).SaleState);
        Assert.Equal(0, await factory.CountPlacesAsync(@event.Id, "Sold"));
    }

    [Fact]
    public async Task Organizers_see_takings_by_category_and_find_orders()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var ayse = await factory.RegisterAsync("Ayşe Kaya");
        using var can = await factory.RegisterAsync("Can Demir");
        var front = await factory.BuyAsync(ayse, @event.Id, Buying.Seats("A-A-1", "A-A-2"));
        var floor = await factory.BuyAsync(can, @event.Id, Buying.Standing(3));
        await can.Http.PostAsync($"/api/orders/{floor.Id}/refund", null);
        var path = $"/api/organizations/{organizer.Id}/events/{@event.Id}";

        var sales = await (await organizer.Owner.Http.GetAsync($"{path}/sales")).ReadAsync<SalesSummaryDto>();
        var byNumber = await (await organizer.Owner.Http.GetAsync($"{path}/orders?q={front.Number}")).ReadAsync<PageDto<OrganizerOrderDto>>();
        var byName = await (await organizer.Owner.Http.GetAsync($"{path}/orders?q=demir")).ReadAsync<PageDto<OrganizerOrderDto>>();
        var refunded = await (await organizer.Owner.Http.GetAsync($"{path}/orders?status=Refunded")).ReadAsync<PageDto<OrganizerOrderDto>>();

        Assert.Equal((2400m, 900m, 1500m), (sales.Gross, sales.Refunded, sales.Net));
        Assert.Equal((1, 2, 1), (sales.PaidOrders, sales.TicketsSold, sales.RefundedOrders));
        Assert.Equal((30, 2, 1500m), (sales.Categories[0].Capacity, sales.Categories[0].Sold, sales.Categories[0].Revenue));
        Assert.Equal((0, 40), (sales.Categories[2].Sold, sales.Categories[2].Available));
        Assert.Equal(2, Assert.Single(sales.Days).Tickets);
        Assert.Equal("Ayşe Kaya", Assert.Single(byNumber.Items).BuyerName);
        Assert.Equal(floor.Number, Assert.Single(byName.Items).Number);
        Assert.Equal(floor.Id, Assert.Single(refunded.Items).Id);
    }

    [Fact]
    public async Task Gate_staff_do_not_see_the_money()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var staff = await factory.RegisterAsync("Kapıcı");
        await organizer.AddMemberAsync(staff, "Staff");

        var response = await staff.Http.GetAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/sales");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
