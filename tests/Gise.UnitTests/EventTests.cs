using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Venues;

namespace Gise.UnitTests;

public sealed class EventTests
{
    [Fact]
    public void A_new_event_is_a_draft_with_prices_in_kurus()
    {
        var @event = Event.CreateDraft(Builders.Organizer, Builders.Hall(), Builders.Details(categories: [("Ön", 750.25m), ("Ayakta", 300m)]), Builders.Now);

        Assert.Equal(EventStatus.Draft, @event.Status);
        Assert.Equal([75_025L, 30_000L], @event.Categories.Select(c => c.PriceMinor));
        Assert.Equal(72, @event.Capacity);
        Assert.Equal("Ön", @event.CategoryOf("A").Name);
        Assert.Empty(@event.Layout);
    }

    [Fact]
    public void Publishing_copies_the_floor_plan_and_lists_one_place_per_seat_and_spot()
    {
        var venue = Builders.Hall();
        var @event = Event.CreateDraft(Builders.Organizer, venue, Builders.Details(), Builders.Now);

        var places = @event.Publish(venue, Builders.Now.AddHours(1));

        Assert.Equal(EventStatus.Published, @event.Status);
        Assert.Equal(72, places.Count);
        Assert.Equal(72, places.Select(p => (p.SectionKey, p.Row, p.Number)).Distinct().Count());
        Assert.Equal(12, places.Count(p => p.Row == "B"));
        Assert.All(places.Where(p => p.SectionKey == "floor"), p => Assert.Null(p.Row));
        Assert.All(places, p => Assert.Equal(PlaceStatus.Available, p.Status));
        Assert.Equal(@event.CategoryOf("floor").Id, places.Last().CategoryId);

        venue.Update("Kültür Merkezi", "Samsun", null, [new Section { Key = "A", Name = "Küçük", Kind = SectionKind.Standing, Capacity = 3 }]);
        Assert.Equal(SectionKind.Seated, @event.Layout[0].Kind);
    }

    [Fact]
    public void Category_ids_survive_editing_a_draft()
    {
        var venue = Builders.Hall();
        var @event = Event.CreateDraft(Builders.Organizer, venue, Builders.Details(), Builders.Now);
        var front = @event.Categories[0].Id;

        @event.Update(venue, Builders.Details(categories: [("ön", 800m), ("Ayakta", 300m)]), Builders.Now);

        Assert.Equal(front, @event.Categories[0].Id);
        Assert.Equal(80_000L, @event.Categories[0].PriceMinor);
    }

    [Fact]
    public void Every_section_needs_a_price_and_every_price_a_section()
    {
        var venue = Builders.Hall();

        var missing = Assert.Throws<DomainException>(() => Event.CreateDraft(Builders.Organizer, venue, Builders.Details(pricing: [("A", "Ön")]), Builders.Now));
        var unused = Assert.Throws<DomainException>(() => Event.CreateDraft(Builders.Organizer, venue,
            Builders.Details(categories: [("Ön", 750m), ("Ayakta", 300m), ("VIP", 2000m)]), Builders.Now));
        var twice = Assert.Throws<DomainException>(() => Event.CreateDraft(Builders.Organizer, venue,
            Builders.Details(pricing: [("A", "Ön"), ("a", "Ayakta"), ("floor", "Ayakta")]), Builders.Now));

        Assert.Contains("floor", missing.Message, StringComparison.Ordinal);
        Assert.Contains("VIP", unused.Message, StringComparison.Ordinal);
        Assert.Contains("priced twice", twice.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void A_buyer_may_get_one_to_ten_tickets(int max) =>
        Assert.Throws<DomainException>(() => Event.CreateDraft(Builders.Organizer, Builders.Hall(), Builders.Details(maxTickets: max), Builders.Now));

    [Fact]
    public void A_waiting_room_needs_a_sensible_pace()
    {
        Assert.Throws<DomainException>(() => Event.CreateDraft(Builders.Organizer, Builders.Hall(), Builders.Details(waitingRoom: true, admitPerMinute: 5), Builders.Now));

        var off = Event.CreateDraft(Builders.Organizer, Builders.Hall(), Builders.Details(waitingRoom: false, admitPerMinute: 5), Builders.Now);
        Assert.Equal(0, off.AdmitPerMinute);
    }

    [Fact]
    public void An_event_whose_start_has_passed_cannot_be_published()
    {
        var venue = Builders.Hall();
        var @event = Event.CreateDraft(Builders.Organizer, venue, Builders.Details(startsAt: Builders.Now.AddHours(2)), Builders.Now);

        var error = Assert.Throws<DomainException>(() => @event.Publish(venue, Builders.Now.AddHours(3)));

        Assert.Equal(ErrorCodes.InvalidState, error.Code);
    }

    [Fact]
    public void The_sale_state_follows_the_clock_and_what_is_left()
    {
        var venue = Builders.Hall();
        var @event = Event.CreateDraft(Builders.Organizer, venue,
            Builders.Details(startsAt: Builders.Now.AddDays(10), saleStartsAt: Builders.Now.AddDays(1)), Builders.Now);
        Assert.Equal(SaleState.Draft, @event.SaleStateAt(Builders.Now, false));

        @event.Publish(venue, Builders.Now);

        Assert.Equal(SaleState.Upcoming, @event.SaleStateAt(Builders.Now, false));
        Assert.Equal(SaleState.OnSale, @event.SaleStateAt(Builders.Now.AddDays(2), false));
        Assert.Equal(SaleState.SoldOut, @event.SaleStateAt(Builders.Now.AddDays(2), true));
        Assert.Equal(SaleState.Ended, @event.SaleStateAt(Builders.Now.AddDays(10), false));

        @event.Cancel(Builders.Now.AddDays(3));
        Assert.Equal(SaleState.Cancelled, @event.SaleStateAt(Builders.Now.AddDays(4), false));
    }

    [Fact]
    public void Refunds_close_a_set_time_before_the_start_or_are_left_to_the_organizer()
    {
        var withCutoff = Event.CreateDraft(Builders.Organizer, Builders.Hall(), Builders.Details(refundCutoffHours: 24), Builders.Now);
        var organizerOnly = Event.CreateDraft(Builders.Organizer, Builders.Hall(), Builders.Details(refundCutoffHours: null), Builders.Now);

        Assert.Equal(withCutoff.StartsAt.AddHours(-24), withCutoff.RefundDeadline);
        Assert.Null(organizerOnly.RefundDeadline);
    }

    [Fact]
    public void Another_organizers_venue_cannot_be_used()
    {
        var error = Assert.Throws<DomainException>(() => Event.CreateDraft(Guid.NewGuid(), Builders.Hall(), Builders.Details(), Builders.Now));

        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(450.5, 45_050)]
    [InlineData(10, 1_000)]
    public void Money_is_kept_in_kurus(double amount, long minor)
    {
        Assert.Equal(minor, Money.ToMinor((decimal)amount));
        Assert.Equal(decimal.Round((decimal)amount, 2), Money.FromMinor(minor));
    }
}
