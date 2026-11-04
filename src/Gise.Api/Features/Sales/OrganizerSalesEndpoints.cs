using System.Security.Claims;
using Gise.Api.Common;
using Gise.Api.Features.Events;
using Gise.Api.Features.Organizations;
using Gise.Api.Features.Tickets;
using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Core.Sales;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Sales;

public sealed record OrganizerOrder(
    Guid Id, string Number, OrderStatus Status, string BuyerName, string BuyerEmail, int Tickets, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, RefundReason? RefundReason);

public sealed record CategorySales(Guid Id, string Name, decimal Price, int Capacity, int Sold, int Held, int Available, decimal Revenue);

public sealed record DailySales(DateOnly Date, int Tickets, decimal Revenue);

public sealed record SalesSummary(
    Guid EventId, string Currency, decimal Gross, decimal Refunded, decimal Net, int PaidOrders, int TicketsSold, int PendingOrders,
    int RefundedOrders, IReadOnlyList<CategorySales> Categories, IReadOnlyList<DailySales> Days);

public sealed record CancelResponse(Guid EventId, int OrdersToRefund, int Refunded);

internal static class OrganizerSalesEndpoints
{
    public static IEndpointRouteBuilder MapOrganizerSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/events/{eventId:guid}").WithTags("Sales (organizer)").RequireAuthorization();
        group.MapGet("/sales", Summary).WithSummary("Takings, tickets by category and sales by day (managers)")
            .WithDescription("Gross counts every paid order, refunded ones included; net is gross minus refunds. Days are UTC.");
        group.MapGet("/orders", Orders).WithSummary("The event's orders (managers)")
            .WithDescription("`q` finds an order number, a buyer's name or email. `status` filters, for example `Paid`.");
        group.MapPost("/orders/{orderId:guid}/refund", Refund).WithSummary("Refund an order at any time (managers)");
        group.MapPost("/cancel", Cancel).WithSummary("Cancel the event and refund everyone (managers)")
            .WithDescription("Sales stop at once. Paid orders are refunded right away where the provider answers, the rest on the next sweep.");
        return app;
    }

    private static async Task<Ok<SalesSummary>> Summary(
        Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var @event = await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
        var now = clock.GetUtcNow();
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.EventId == eventId && o.PaidAt != null)
            .Select(o => new { o.Status, o.AmountMinor, o.PaidAt, o.Lines })
            .ToListAsync(ct);
        var pending = await db.Orders.CountAsync(o => o.EventId == eventId && o.Status == OrderStatus.PendingPayment, ct);
        var places = await db.Places.AsNoTracking().Where(p => p.EventId == eventId)
            .GroupBy(p => p.CategoryId)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Sold = g.Count(p => p.Status == PlaceStatus.Sold),
                Held = g.Count(p => p.Status == PlaceStatus.Held && p.HeldUntil > now),
            })
            .ToListAsync(ct);

        var kept = orders.Where(o => o.Status == OrderStatus.Paid).ToList();
        var refunded = orders.Where(o => o.Status is OrderStatus.RefundPending or OrderStatus.Refunded).ToList();
        var categories = @event.Categories.OrderBy(c => c.Position).Select(c =>
        {
            var counts = places.FirstOrDefault(p => p.Key == c.Id);
            var revenue = kept.SelectMany(o => o.Lines).Where(l => l.CategoryId == c.Id).Sum(l => l.PriceMinor);
            return new CategorySales(c.Id, c.Name, Money.FromMinor(c.PriceMinor), counts?.Total ?? 0, counts?.Sold ?? 0, counts?.Held ?? 0,
                (counts?.Total ?? 0) - (counts?.Sold ?? 0) - (counts?.Held ?? 0), Money.FromMinor(revenue));
        }).ToList();
        var days = kept
            .GroupBy(o => DateOnly.FromDateTime(o.PaidAt!.Value.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DailySales(g.Key, g.Sum(o => o.Lines.Count), Money.FromMinor(g.Sum(o => o.AmountMinor))))
            .ToList();

        var gross = orders.Sum(o => o.AmountMinor);
        var back = refunded.Sum(o => o.AmountMinor);
        return TypedResults.Ok(new SalesSummary(
            eventId, Money.Currency, Money.FromMinor(gross), Money.FromMinor(back), Money.FromMinor(gross - back), kept.Count,
            kept.Sum(o => o.Lines.Count), pending, refunded.Count, categories, days));
    }

    private static async Task<Ok<Page<OrganizerOrder>>> Orders(
        Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct,
        string? q = null, OrderStatus? status = null, int? page = null, int? pageSize = null)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
        var (number, size) = Paging.Clamp(page, pageSize);
        var query = from o in db.Orders.AsNoTracking()
                    join u in db.Users on o.UserId equals u.Id
                    where o.EventId == eventId && o.Status != OrderStatus.Failed
                    select new { Order = o, u.DisplayName, u.Email };
        if (status is { } s)
        {
            query = query.Where(x => x.Order.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            query = query.Where(x => EF.Functions.ILike(x.Order.Number, pattern) || EF.Functions.ILike(x.DisplayName, pattern) || EF.Functions.ILike(x.Email, pattern));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Order.CreatedAt).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        var items = rows.Select(x => new OrganizerOrder(
            x.Order.Id, x.Order.Number, x.Order.Status, x.DisplayName, x.Email, x.Order.Lines.Count, Money.FromMinor(x.Order.AmountMinor),
            x.Order.Currency, x.Order.CreatedAt, x.Order.PaidAt, x.Order.RefundReason)).ToList();
        return TypedResults.Ok(new Page<OrganizerOrder>(items, total, number, size));
    }

    private static async Task<Ok<OrganizerOrder>> Refund(
        Guid organizationId, Guid eventId, Guid orderId, ClaimsPrincipal user, GiseDbContext db, OrderService orders, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.EventId == eventId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such order for this event.");
        await orders.RequestRefundAsync(order, RefundReason.Organizer, ct);
        var buyer = await db.Users.AsNoTracking().SingleAsync(u => u.Id == order.UserId, ct);
        return TypedResults.Ok(new OrganizerOrder(order.Id, order.Number, order.Status, buyer.DisplayName, buyer.Email, order.Lines.Count,
            Money.FromMinor(order.AmountMinor), order.Currency, order.CreatedAt, order.PaidAt, order.RefundReason));
    }

    private static async Task<Ok<CancelResponse>> Cancel(
        Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, OrderService orders, TicketBook tickets, TimeProvider clock,
        CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var now = clock.GetUtcNow();
        List<Order> paid;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM events WHERE id = {eventId} AND organization_id = {organizationId} FOR UPDATE", ct);
            var @event = await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
            @event.Cancel(now);

            // Nobody can buy any more: drop every basket, then mark each paid order for refund.
            await db.Holds.Where(h => h.EventId == eventId && h.Status == HoldStatus.Active)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Released), ct);
            await db.Places.Where(p => p.EventId == eventId && p.Status == PlaceStatus.Held && p.HeldUntil > now && !db.Orders.Any(o => o.HoldId == p.HoldId))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PlaceStatus.Available).SetProperty(p => p.HoldId, (Guid?)null).SetProperty(p => p.HeldUntil, (DateTimeOffset?)null), ct);
            await db.Places.Where(p => p.EventId == eventId && p.Status == PlaceStatus.Sold)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PlaceStatus.Available).SetProperty(p => p.HoldId, (Guid?)null), ct);
            paid = await db.Orders.Where(o => o.EventId == eventId && o.Status == OrderStatus.Paid).ToListAsync(ct);
            foreach (var order in paid)
            {
                order.RequestRefund(RefundReason.EventCancelled, now);
                await tickets.VoidAsync(order, now, ct);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var refunded = 0;
        foreach (var order in paid)
        {
            if (await orders.TryRefundAsync(order, ct))
            {
                refunded++;
            }
        }

        return TypedResults.Ok(new CancelResponse(eventId, paid.Count, refunded));
    }
}
