using Gise.Core.Sales;
using Gise.Infrastructure.Payments;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Sales;

public sealed record ReconcileResult(int HoldsExpired, int OrdersSettled, int OrdersExpired, int Refunded);

/// <summary>
/// The sweep behind the scenes: unpaid orders past their deadline are checked with the provider (a webhook may
/// have been lost) and closed, run-out holds are tidied, and refunds the provider did not take yet are retried.
/// Nothing here is needed for correctness of a sale; it keeps the books tidy and the money moving.
/// </summary>
internal sealed partial class SalesReconciler(IServiceScopeFactory scopes, TimeProvider clock, IOptions<SalesOptions> options, ILogger<SalesReconciler> logger)
    : BackgroundService
{
    private const int Batch = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ReconcileSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ReconcileSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await ReconcileOnceAsync(stoppingToken);
                if (result != new ReconcileResult(0, 0, 0, 0))
                {
                    LogSwept(logger, result.HoldsExpired, result.OrdersSettled, result.OrdersExpired, result.Refunded);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    public async Task<ReconcileResult> ReconcileOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<GiseDbContext>();
        var gateway = services.GetRequiredService<IPaymentGateway>();
        var processor = services.GetRequiredService<PaymentProcessor>();
        var orders = services.GetRequiredService<OrderService>();
        var now = clock.GetUtcNow();

        var holds = await db.Holds.Where(h => h.Status == HoldStatus.Active && h.ExpiresAt <= now).OrderBy(h => h.ExpiresAt).Take(Batch).ToListAsync(ct);
        foreach (var hold in holds)
        {
            hold.Expire();
            await PlaceClaims.ReleaseHeldAsync(db, hold.Id, ct);
        }

        await db.SaveChangesAsync(ct);

        int settled = 0, expired = 0;
        var overdue = now.AddSeconds(-options.Value.PaymentGraceSeconds);
        var unpaid = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentDeadline <= overdue)
            .OrderBy(o => o.PaymentDeadline).Take(Batch).Select(o => new { o.Id, o.PaymentId }).ToListAsync(ct);
        foreach (var candidate in unpaid)
        {
            var state = PaymentState.Pending;
            if (candidate.PaymentId is not null)
            {
                try
                {
                    state = await gateway.GetStateAsync(candidate.PaymentId, ct);
                }
                catch (PaymentProviderException)
                {
                    // Leave it for the next sweep rather than expire a payment that may have gone through.
                    continue;
                }
            }

            if (state == PaymentState.Succeeded)
            {
                await processor.ApplyAsync(gateway.Name, new PaymentNotice($"poll-{candidate.Id:N}", "payment.succeeded", candidate.PaymentId!, candidate.Id, state), ct);
                settled++;
                continue;
            }

            if (await ExpireAsync(db, candidate.Id, now, ct))
            {
                expired++;
            }
        }

        var refunded = 0;
        var refunds = await db.Orders.Where(o => o.Status == OrderStatus.RefundPending).OrderBy(o => o.RefundRequestedAt).Take(Batch).ToListAsync(ct);
        foreach (var order in refunds)
        {
            if (await orders.TryRefundAsync(order, ct))
            {
                refunded++;
            }
        }

        return new ReconcileResult(holds.Count, settled, expired, refunded);
    }

    private static async Task<bool> ExpireAsync(GiseDbContext db, Guid orderId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM orders WHERE id = {orderId} FOR UPDATE", ct);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, ct);
        if (order.Status != OrderStatus.PendingPayment)
        {
            return false;
        }

        order.MarkExpired(now);
        await PlaceClaims.ReleaseHeldAsync(db, order.HoldId, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sales sweep: {Holds} holds ran out, {Settled} late payments settled, {Expired} orders expired, {Refunded} refunds done")]
    private static partial void LogSwept(ILogger logger, int holds, int settled, int expired, int refunded);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sales sweep failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
