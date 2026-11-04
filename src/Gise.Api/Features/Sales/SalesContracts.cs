using Gise.Core.Events;
using Gise.Core.Sales;

namespace Gise.Api.Features.Sales;

public sealed record QueueStatus(
    Guid EventId,
    bool WaitingRoom,
    bool Admitted,
    long? Position,
    long? Ahead,
    DateTimeOffset SaleStartsAt,
    DateTimeOffset? AdmittedAt,
    DateTimeOffset? ShopUntil,
    int? EstimatedWaitSeconds,
    int AdmitPerMinute);

public sealed record SeatRequest(string Section, string Row, int Number);

public sealed record StandingRequest(string Section, int Quantity);

public sealed record HoldRequest(List<SeatRequest>? Seats, List<StandingRequest>? Standing);

/// <summary>A place in a basket or on an order, ready to show: "Parter · Row C · Seat 12", "Ayakta".</summary>
public sealed record LineResponse(Guid PlaceId, string Section, string SectionName, string? Row, int Number, Guid CategoryId, string Category, decimal Price);

public sealed record HoldResponse(
    Guid Id, Guid EventId, HoldStatus Status, DateTimeOffset ExpiresAt, IReadOnlyList<LineResponse> Lines, decimal Total, string Currency);

public sealed record OrderEventInfo(Guid Id, string Title, string VenueName, string City, DateTimeOffset StartsAt, EventStatus Status);

public sealed record OrderResponse(
    Guid Id,
    string Number,
    OrderStatus Status,
    OrderEventInfo Event,
    IReadOnlyList<LineResponse> Lines,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset PaymentDeadline,
    string? CheckoutUrl,
    DateTimeOffset? PaidAt,
    RefundReason? RefundReason,
    DateTimeOffset? RefundRequestedAt,
    DateTimeOffset? RefundedAt,
    bool CanRefund,
    DateTimeOffset? RefundDeadline);

public sealed record OrderSummary(
    Guid Id, string Number, OrderStatus Status, Guid EventId, string EventTitle, string VenueName, DateTimeOffset StartsAt,
    int Tickets, decimal Amount, string Currency, DateTimeOffset CreatedAt);

internal static class SalesMapping
{
    public static LineResponse ToLine(OrderLine l) =>
        new(l.PlaceId, l.SectionKey, l.SectionName, l.Row, l.Number, l.CategoryId, l.CategoryName, Money.FromMinor(l.PriceMinor));

    public static OrderResponse ToResponse(Order order, Event @event, DateTimeOffset now) => new(
        order.Id,
        order.Number,
        order.Status,
        new OrderEventInfo(@event.Id, @event.Title, @event.VenueName, @event.City, @event.StartsAt, @event.Status),
        order.Lines.Select(ToLine).ToList(),
        Money.FromMinor(order.AmountMinor),
        order.Currency,
        order.CreatedAt,
        order.PaymentDeadline,
        order.Status == OrderStatus.PendingPayment ? order.CheckoutUrl : null,
        order.PaidAt,
        order.RefundReason,
        order.RefundRequestedAt,
        order.RefundedAt,
        order.Status == OrderStatus.Paid && @event.Status == EventStatus.Published && @event.RefundDeadline is { } deadline && now <= deadline,
        @event.RefundDeadline);

    /// <summary>The order lines for a set of places, named as the event names them.</summary>
    public static List<OrderLine> Lines(Event @event, IEnumerable<Place> places) =>
        places
            .OrderBy(p => @event.Layout.FindIndex(s => s.Key == p.SectionKey))
            .ThenBy(p => p.Row is null ? -1 : @event.Layout.First(s => s.Key == p.SectionKey).Rows.FindIndex(r => r.Label == p.Row))
            .ThenBy(p => p.Number)
            .Select(p =>
            {
                var category = @event.Categories.Single(c => c.Id == p.CategoryId);
                return new OrderLine
                {
                    PlaceId = p.Id,
                    SectionKey = p.SectionKey,
                    SectionName = @event.Layout.First(s => s.Key == p.SectionKey).Name,
                    Row = p.Row,
                    Number = p.Number,
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    PriceMinor = category.PriceMinor,
                };
            })
            .ToList();
}
