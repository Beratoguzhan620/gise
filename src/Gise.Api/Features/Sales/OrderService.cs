using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Sales;
using Gise.Infrastructure.Payments;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Sales;

/// <summary>Turns a hold into an order the buyer pays at the provider, and gives money back when an order is refunded.</summary>
internal sealed partial class OrderService(
    GiseDbContext db, IPaymentGateway gateway, TimeProvider clock, IOptions<PaymentOptions> payments, ILogger<OrderService> logger)
{
    public async Task<(Order Order, bool Created)> CheckoutAsync(Event @event, Guid holdId, Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // A double click on "Pay" waits here and then finds the order the first click made.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM holds WHERE id = {holdId} FOR UPDATE", ct);
        var hold = await db.Holds.SingleOrDefaultAsync(h => h.Id == holdId && h.UserId == userId && h.EventId == @event.Id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such basket among yours.");
        // The caller may have read the hold before the lock; what counts is how it is now.
        await db.Entry(hold).ReloadAsync(ct);

        if (hold.Status == HoldStatus.Ordered)
        {
            return (await db.Orders.SingleAsync(o => o.HoldId == hold.Id, ct), false);
        }

        HoldService.EnsureOnSale(@event, now);
        var deadline = now.AddMinutes(payments.Value.PaymentMinutes);
        var places = await db.Places.AsNoTracking().Where(p => p.HoldId == hold.Id && p.Status == PlaceStatus.Held && p.HeldUntil > now).ToListAsync(ct);
        hold.Order(now, deadline);
        var order = Order.Create(hold, SalesMapping.Lines(@event, places), now, deadline);
        if (await PlaceClaims.ExtendAsync(db, hold.Id, deadline, ct) != hold.Places)
        {
            throw new DomainException(ErrorCodes.HoldExpired, "Some of the places are no longer set aside for you. Choose again.");
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        try
        {
            var session = await gateway.CreatePaymentAsync(new PaymentRequest(
                order.Id, order.Number, order.AmountMinor, order.Currency, @event.Title,
                payments.Value.ReturnUrl.Replace("{orderId}", order.Id.ToString(), StringComparison.Ordinal), deadline), ct);
            order.AttachPayment(gateway.Name, session.PaymentId, session.CheckoutUrl);
            await db.SaveChangesAsync(ct);
        }
        catch (PaymentProviderException ex)
        {
            LogProviderDown(logger, order.Id, ex.Message);
            order.MarkFailed(clock.GetUtcNow());
            await PlaceClaims.ReleaseHeldAsync(db, hold.Id, ct);
            await db.SaveChangesAsync(ct);
            throw new DomainException(ErrorCodes.PaymentUnavailable, "The payment service is not answering. Nothing was charged; try again in a few minutes.");
        }

        LogOrdered(logger, order.Id, order.Number, order.Lines.Count);
        return (order, true);
    }

    /// <summary>Marks a paid order for refund and frees its places at once; the money goes back now or on the next sweep.</summary>
    public async Task<Order> RequestRefundAsync(Order order, RefundReason reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM orders WHERE id = {order.Id} FOR UPDATE", ct);
            await db.Entry(order).ReloadAsync(ct);
            order.RequestRefund(reason, now);
            await PlaceClaims.ReleaseSoldAsync(db, order.HoldId, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await TryRefundAsync(order, ct);
        return order;
    }

    /// <summary>Asks the provider to return the money. A failure leaves the order waiting for the next sweep.</summary>
    public async Task<bool> TryRefundAsync(Order order, CancellationToken ct)
    {
        if (order.Status != OrderStatus.RefundPending || order.PaymentId is null)
        {
            return false;
        }

        try
        {
            // Keyed by order: if we time out after the provider refunded, the retry does not refund twice.
            await gateway.RefundAsync(order.PaymentId, order.AmountMinor, $"refund-{order.Id:N}", ct);
        }
        catch (PaymentProviderException ex)
        {
            LogRefundFailed(logger, order.Id, ex.Message);
            return false;
        }

        order.MarkRefunded(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        LogRefunded(logger, order.Id, order.RefundReason);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} ({OrderNumber}) waiting for payment: {Places} places")]
    private static partial void LogOrdered(ILogger logger, Guid orderId, string orderNumber, int places);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment provider unavailable for order {OrderId}: {Reason}")]
    private static partial void LogProviderDown(ILogger logger, Guid orderId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refund for order {OrderId} not done yet: {Reason}")]
    private static partial void LogRefundFailed(ILogger logger, Guid orderId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} refunded ({Reason})")]
    private static partial void LogRefunded(ILogger logger, Guid orderId, RefundReason? reason);
}
