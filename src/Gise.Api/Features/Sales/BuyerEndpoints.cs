using System.Security.Claims;
using Gise.Api.Common;
using Gise.Api.Features.Events;
using Gise.Core;
using Gise.Core.Sales;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Sales;

/// <summary>What a buyer does: take places, check out, see and refund their orders.</summary>
internal static class BuyerEndpoints
{
    public const string RateLimit = "holds";

    public static IEndpointRouteBuilder MapBuyerEndpoints(this IEndpointRouteBuilder app)
    {
        var holds = app.MapGroup("/api").WithTags("Buying").RequireAuthorization();
        holds.MapPost("/events/{eventId:guid}/holds", Hold).RequireRateLimiting(RateLimit).WithSummary("Set places aside for a few minutes")
            .WithDescription("""
                `seats` names seats in seated sections (`section`, `row`, `number`); `standing` asks for a number of spots in a
                standing area. Either every place is taken or none is: if any seat was taken a moment ago, the answer is
                `409 place_unavailable` naming it. Choosing again replaces your earlier basket for the event.
                """);
        holds.MapGet("/holds/{holdId:guid}", GetHold).WithSummary("Your basket");
        holds.MapDelete("/holds/{holdId:guid}", Release).WithSummary("Let the places go");
        holds.MapPost("/holds/{holdId:guid}/checkout", Checkout).WithSummary("Check out: create the order and get the payment page")
            .WithDescription("Send the buyer to `checkoutUrl`. The places stay set aside until `paymentDeadline`. Calling this twice returns the same order.");

        holds.MapGet("/orders", List).WithSummary("Your orders, newest first");
        holds.MapGet("/orders/{orderId:guid}", Get).WithSummary("One of your orders");
        holds.MapPost("/orders/{orderId:guid}/refund", Refund).WithSummary("Refund a paid order")
            .WithDescription("Open until the event's refund deadline. The places go back on sale at once; the money follows when the provider confirms.");
        return app;
    }

    private static async Task<Created<HoldResponse>> Hold(
        Guid eventId, HoldRequest request, ClaimsPrincipal user, GiseDbContext db, HoldService holds, CancellationToken ct)
    {
        var @event = await CatalogEndpoints.FindPublicAsync(db, eventId, ct);
        var hold = await holds.CreateAsync(@event, user.UserId(), request, ct);
        return TypedResults.Created($"/api/holds/{hold.Id}", hold);
    }

    private static async Task<Ok<HoldResponse>> GetHold(Guid holdId, ClaimsPrincipal user, GiseDbContext db, HoldService holds, CancellationToken ct)
    {
        var hold = await FindHoldAsync(db, holdId, user, ct);
        var @event = await CatalogEndpoints.FindPublicAsync(db, hold.EventId, ct);
        return TypedResults.Ok(await holds.ResponseAsync(@event, hold, ct));
    }

    private static async Task<NoContent> Release(Guid holdId, ClaimsPrincipal user, GiseDbContext db, HoldService holds, CancellationToken ct)
    {
        await holds.ReleaseAsync(await FindHoldAsync(db, holdId, user, ct), ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<OrderResponse>, Ok<OrderResponse>>> Checkout(
        Guid holdId, ClaimsPrincipal user, GiseDbContext db, OrderService orders, TimeProvider clock, CancellationToken ct)
    {
        var hold = await FindHoldAsync(db, holdId, user, ct);
        var @event = await CatalogEndpoints.FindPublicAsync(db, hold.EventId, ct);
        var (order, created) = await orders.CheckoutAsync(@event, holdId, user.UserId(), ct);
        var response = SalesMapping.ToResponse(order, @event, clock.GetUtcNow());
        return created ? TypedResults.Created($"/api/orders/{order.Id}", response) : TypedResults.Ok(response);
    }

    private static async Task<Ok<Page<OrderSummary>>> List(ClaimsPrincipal user, GiseDbContext db, CancellationToken ct, int? page = null, int? pageSize = null)
    {
        var (number, size) = Paging.Clamp(page, pageSize);
        var userId = user.UserId();
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(o => o.CreatedAt).Skip((number - 1) * size).Take(size)
            .Join(db.Events, o => o.EventId, e => e.Id, (o, e) => new { Order = o, e.Title, e.VenueName, e.StartsAt })
            .ToListAsync(ct);
        var items = rows.Select(r => new OrderSummary(
            r.Order.Id, r.Order.Number, r.Order.Status, r.Order.EventId, r.Title, r.VenueName, r.StartsAt, r.Order.Lines.Count,
            Gise.Core.Events.Money.FromMinor(r.Order.AmountMinor), r.Order.Currency, r.Order.CreatedAt)).ToList();
        return TypedResults.Ok(new Page<OrderSummary>(items, total, number, size));
    }

    private static async Task<Ok<OrderResponse>> Get(Guid orderId, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var order = await FindOrderAsync(db, orderId, user, ct);
        var @event = await db.Events.AsNoTracking().SingleAsync(e => e.Id == order.EventId, ct);
        return TypedResults.Ok(SalesMapping.ToResponse(order, @event, clock.GetUtcNow()));
    }

    private static async Task<Ok<OrderResponse>> Refund(Guid orderId, ClaimsPrincipal user, GiseDbContext db, OrderService orders, TimeProvider clock, CancellationToken ct)
    {
        var order = await FindOrderAsync(db, orderId, user, ct);
        var @event = await db.Events.AsNoTracking().SingleAsync(e => e.Id == order.EventId, ct);
        order.EnsureBuyerMayRefund(@event, clock.GetUtcNow());
        await orders.RequestRefundAsync(order, RefundReason.Buyer, ct);
        return TypedResults.Ok(SalesMapping.ToResponse(order, @event, clock.GetUtcNow()));
    }

    private static async Task<Hold> FindHoldAsync(GiseDbContext db, Guid holdId, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        return await db.Holds.SingleOrDefaultAsync(h => h.Id == holdId && h.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such basket among yours.");
    }

    private static async Task<Order> FindOrderAsync(GiseDbContext db, Guid orderId, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        return await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such order among yours.");
    }
}
