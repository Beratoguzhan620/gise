using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Queue;
using Gise.Core.Sales;

namespace Gise.UnitTests;

public sealed class SalesTests
{
    private static readonly DateTimeOffset Opening = Builders.Now;

    [Fact]
    public void The_line_moves_a_fixed_number_of_people_a_minute()
    {
        Assert.Equal(0, WaitingRoom.AdmittedThrough(Opening.AddSeconds(-1), Opening, 100));
        Assert.Equal(100, WaitingRoom.AdmittedThrough(Opening, Opening, 100));
        Assert.Equal(150, WaitingRoom.AdmittedThrough(Opening.AddSeconds(30), Opening, 100));
        Assert.Equal(1100, WaitingRoom.AdmittedThrough(Opening.AddMinutes(10), Opening, 100));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100, 0)]
    [InlineData(101, 1)]
    [InlineData(150, 30)]
    [InlineData(1100, 600)]
    public void Everyone_knows_when_their_turn_comes(long position, int seconds)
    {
        Assert.Equal(Opening.AddSeconds(seconds), WaitingRoom.AdmittedAt(position, Opening, 100));
        Assert.True(WaitingRoom.AdmittedThrough(Opening.AddSeconds(seconds), Opening, 100) >= position);
    }

    [Fact]
    public void Arriving_early_gives_a_random_place_before_everyone_who_comes_later()
    {
        var random = new Random(7);
        var early = Enumerable.Range(0, 50).Select(i => WaitingRoom.OrderKey(Opening.AddMinutes(-i), Opening, random)).ToList();
        var late = WaitingRoom.OrderKey(Opening.AddSeconds(1), Opening, random);
        var later = WaitingRoom.OrderKey(Opening.AddSeconds(2), Opening, random);

        Assert.All(early, key => Assert.InRange(key, 0, 1));
        Assert.NotEqual(early.Order(), early);
        Assert.True(early.Max() < late && late < later);
    }

    [Fact]
    public void A_hold_runs_out_and_cannot_be_checked_out_afterwards()
    {
        var hold = Hold.Create(Guid.NewGuid(), Guid.NewGuid(), 2, Opening, TimeSpan.FromMinutes(8));

        Assert.True(hold.IsActive(Opening.AddMinutes(7)));
        Assert.False(hold.IsActive(Opening.AddMinutes(8)));
        Assert.Equal(ErrorCodes.HoldExpired, Assert.Throws<DomainException>(() => hold.Order(Opening.AddMinutes(9), Opening.AddMinutes(19))).Code);

        hold.Order(Opening.AddMinutes(5), Opening.AddMinutes(15));
        Assert.Equal((HoldStatus.Ordered, Opening.AddMinutes(15)), (hold.Status, hold.ExpiresAt));
    }

    [Fact]
    public void An_order_totals_its_lines_and_gets_a_readable_number()
    {
        var hold = Hold.Create(Guid.NewGuid(), Guid.NewGuid(), 2, Opening, TimeSpan.FromMinutes(8));
        var order = Order.Create(hold, [Line(75_000), Line(45_050)], Opening, Opening.AddMinutes(10));

        Assert.Equal((120_050L, OrderStatus.PendingPayment), (order.AmountMinor, order.Status));
        Assert.Matches("^GS-[2-9A-HJKMNP-Z]{8}$", order.Number);
        Assert.Throws<DomainException>(() => Order.Create(hold, [Line(75_000)], Opening, Opening.AddMinutes(10)));
    }

    [Fact]
    public void A_paid_order_is_refunded_once()
    {
        var order = Paid();

        order.RequestRefund(RefundReason.Buyer, Opening);
        var twice = Assert.Throws<DomainException>(() => order.RequestRefund(RefundReason.Buyer, Opening));
        order.MarkRefunded(Opening.AddMinutes(1));

        Assert.Contains("already", twice.Message, StringComparison.Ordinal);
        Assert.Equal((OrderStatus.Refunded, RefundReason.Buyer), (order.Status, order.RefundReason));
    }

    [Fact]
    public void Payment_outcomes_only_move_an_order_forward()
    {
        var order = Paid();

        order.MarkFailed(Opening);
        order.MarkExpired(Opening);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.False(order.MarkPaid(Opening));
    }

    [Fact]
    public void An_expired_order_can_still_be_settled_by_a_late_payment()
    {
        var hold = Hold.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Opening, TimeSpan.FromMinutes(8));
        var order = Order.Create(hold, [Line(1000)], Opening, Opening.AddMinutes(10));
        order.MarkExpired(Opening.AddMinutes(11));

        Assert.True(order.MarkPaid(Opening.AddMinutes(12)));
        Assert.Null(order.ClosedAt);
    }

    [Fact]
    public void Buyers_refund_until_the_cutoff_unless_the_organizer_keeps_refunds()
    {
        var venue = Builders.Hall();
        var withCutoff = Event.CreateDraft(Builders.Organizer, venue, Builders.Details(startsAt: Opening.AddDays(3), refundCutoffHours: 48), Opening);
        var organizerOnly = Event.CreateDraft(Builders.Organizer, venue, Builders.Details(refundCutoffHours: null), Opening);
        var order = Paid();

        order.EnsureBuyerMayRefund(withCutoff, Opening.AddDays(1));
        var closed = Assert.Throws<DomainException>(() => order.EnsureBuyerMayRefund(withCutoff, Opening.AddDays(1).AddMinutes(1)));
        var kept = Assert.Throws<DomainException>(() => order.EnsureBuyerMayRefund(organizerOnly, Opening));

        Assert.Equal((ErrorCodes.RefundClosed, ErrorCodes.RefundClosed), (closed.Code, kept.Code));
    }

    private static Order Paid()
    {
        var hold = Hold.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Opening, TimeSpan.FromMinutes(8));
        var order = Order.Create(hold, [Line(1000)], Opening, Opening.AddMinutes(10));
        order.MarkPaid(Opening.AddMinutes(1));
        return order;
    }

    private static OrderLine Line(long price) => new() { PlaceId = Guid.NewGuid(), SectionKey = "A", SectionName = "Parter", Row = "A", Number = 1, CategoryName = "Ön", PriceMinor = price };
}
