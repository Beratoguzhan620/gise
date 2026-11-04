using Gise.Api.Features.Tickets;
using Gise.Core.Events;
using Gise.Core.Sales;
using Gise.Infrastructure.Payments;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Sales;

public enum NoticeOutcome
{
    Applied = 0,

    /// <summary>Seen before; providers deliver at least once.</summary>
    Duplicate = 1,

    /// <summary>About an order or payment we do not know; recorded and otherwise ignored.</summary>
    Ignored = 2,
}

/// <summary>
/// Acts on what the payment provider reports. Each notice is recorded in the same transaction that acts on it,
/// so a notice delivered twice, or a webhook and a poll that both report the same payment, change things once.
/// </summary>
internal sealed partial class PaymentProcessor(GiseDbContext db, TicketBook tickets, TimeProvider clock, ILogger<PaymentProcessor> logger)
{
    public async Task<NoticeOutcome> ApplyAsync(string provider, PaymentNotice notice, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO processed_notices (provider, notice_id, type, order_id, received_at)
            VALUES ({provider}, {notice.EventId}, {notice.Type}, {notice.OrderId}, {now})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            return NoticeOutcome.Duplicate;
        }

        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM orders WHERE id = {notice.OrderId} FOR UPDATE", ct);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == notice.OrderId, ct);
        if (order is null || order.PaymentId != notice.PaymentId)
        {
            LogUnknown(logger, notice.EventId, notice.OrderId);
            await transaction.CommitAsync(ct);
            return NoticeOutcome.Ignored;
        }

        switch (notice.State)
        {
            case PaymentState.Succeeded:
                await SettleAsync(order, now, ct);
                break;
            case PaymentState.Failed when order.Status == OrderStatus.PendingPayment:
                order.MarkFailed(now);
                await PlaceClaims.ReleaseHeldAsync(db, order.HoldId, ct);
                break;
            case PaymentState.Refunded:
                order.MarkRefunded(now);
                break;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        LogApplied(logger, notice.Type, order.Id, order.Status);
        return NoticeOutcome.Applied;
    }

    private async Task SettleAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        if (order.Status is not (OrderStatus.PendingPayment or OrderStatus.Expired or OrderStatus.Failed))
        {
            return;
        }

        var @event = await db.Events.AsNoTracking().SingleAsync(e => e.Id == order.EventId, ct);
        if (@event.Status == EventStatus.Cancelled)
        {
            await PlaceClaims.ReleaseHeldAsync(db, order.HoldId, ct);
            order.RefundLatePayment(now);
            await tickets.VoidAsync(order, now, ct);
            return;
        }

        // On time, the places are still held for this order; late, they may have gone back on sale or to someone else.
        var sold = order.Status == OrderStatus.PendingPayment
            ? await PlaceClaims.SellAsync(db, order.HoldId, ct)
            : await PlaceClaims.ReclaimAsync(db, order.HoldId, order.Lines.Select(l => l.PlaceId).ToArray(), now, ct);
        if (sold == order.Lines.Count)
        {
            order.MarkPaid(now);
            await tickets.IssueAsync(order, now, ct);
            return;
        }

        // Some places went to someone else: none of them is sold to this buyer, and their money goes back.
        await PlaceClaims.ReleaseSoldAsync(db, order.HoldId, ct);
        order.RefundLatePayment(now);
        await tickets.VoidAsync(order, now, ct);
        LogLate(logger, order.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment notice {Type} applied to order {OrderId}: now {Status}")]
    private static partial void LogApplied(ILogger logger, string type, Guid orderId, OrderStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment notice {NoticeId} names an unknown order or payment ({OrderId})")]
    private static partial void LogUnknown(ILogger logger, string noticeId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} was paid after its places were sold elsewhere; refunding")]
    private static partial void LogLate(ILogger logger, Guid orderId);
}
