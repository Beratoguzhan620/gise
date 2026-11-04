using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Queue;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Sales;

/// <summary>Places buyers in an event's waiting room and tells them when it is their turn.</summary>
internal sealed class QueueService(GiseDbContext db, TimeProvider clock, IOptions<SalesOptions> options)
{
    public async Task<QueueStatus> JoinAsync(Event @event, Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!@event.WaitingRoom)
        {
            return Open(@event);
        }

        EnsureSelling(@event, now);
        var entry = await db.QueueEntries.SingleOrDefaultAsync(q => q.EventId == @event.Id && q.UserId == userId, ct);
        if (entry is null)
        {
            entry = QueueEntry.Join(@event.Id, userId, @event.SaleStartsAt, now, Random.Shared);
            db.QueueEntries.Add(entry);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // The same person joined from two tabs at once; the first one counts.
                db.ChangeTracker.Clear();
                entry = await db.QueueEntries.SingleAsync(q => q.EventId == @event.Id && q.UserId == userId, ct);
            }
        }
        else if (entry.AdmittedAt is { } admittedAt && admittedAt.AddMinutes(options.Value.ShoppingMinutes) <= now)
        {
            // Their turn came and went: back of the line, with a new place in the order.
            entry.Rejoin(@event.SaleStartsAt, now, Random.Shared);
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE queue_entries SET sequence = nextval('queue_sequence') WHERE event_id = {@event.Id} AND user_id = {userId}", ct);
            await db.Entry(entry).ReloadAsync(ct);
        }

        return await StatusAsync(@event, entry, now, ct);
    }

    public async Task<QueueStatus> StatusAsync(Event @event, Guid userId, CancellationToken ct)
    {
        if (!@event.WaitingRoom)
        {
            return Open(@event);
        }

        var entry = await db.QueueEntries.SingleOrDefaultAsync(q => q.EventId == @event.Id && q.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "You have not joined this event's waiting room.");
        return await StatusAsync(@event, entry, clock.GetUtcNow(), ct);
    }

    /// <summary>Lets a buyer take places only when it is their turn and their shopping time has not run out.</summary>
    public async Task EnsureAdmittedAsync(Event @event, Guid userId, CancellationToken ct)
    {
        if (!@event.WaitingRoom)
        {
            return;
        }

        var entry = await db.QueueEntries.SingleOrDefaultAsync(q => q.EventId == @event.Id && q.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.QueueRequired, "This sale has a waiting room. Join it first; you will be let in when it is your turn.");
        var status = await StatusAsync(@event, entry, clock.GetUtcNow(), ct);
        if (!status.Admitted)
        {
            throw new DomainException(ErrorCodes.QueueRequired, status.ShopUntil is null
                ? $"It is not your turn yet: you are number {status.Position:N0} in line."
                : "Your time to choose ran out. Join the waiting room again.");
        }
    }

    private async Task<QueueStatus> StatusAsync(Event @event, QueueEntry entry, DateTimeOffset now, CancellationToken ct)
    {
        var ahead = await db.QueueEntries.CountAsync(q => q.EventId == @event.Id
            && (q.OrderKey < entry.OrderKey || (q.OrderKey == entry.OrderKey && q.Sequence < entry.Sequence)), ct);
        var position = ahead + 1L;
        var reached = position <= WaitingRoom.AdmittedThrough(now, @event.SaleStartsAt, @event.AdmitPerMinute);
        if (reached && entry.AdmittedAt is null)
        {
            entry.Admit(now);
            await db.SaveChangesAsync(ct);
        }

        DateTimeOffset? shopUntil = entry.AdmittedAt?.AddMinutes(options.Value.ShoppingMinutes);
        var admitted = reached && shopUntil > now;
        var wait = reached ? (int?)null : (int)Math.Max(0, Math.Ceiling((WaitingRoom.AdmittedAt(position, @event.SaleStartsAt, @event.AdmitPerMinute) - now).TotalSeconds));
        return new QueueStatus(@event.Id, true, admitted, position, ahead, @event.SaleStartsAt, entry.AdmittedAt, shopUntil, wait, @event.AdmitPerMinute);
    }

    private static QueueStatus Open(Event @event) =>
        new(@event.Id, false, true, null, null, @event.SaleStartsAt, null, null, null, 0);

    private static void EnsureSelling(Event @event, DateTimeOffset now)
    {
        var state = @event.SaleStateAt(now, false);
        if (state is not (SaleState.OnSale or SaleState.Upcoming))
        {
            throw new DomainException(ErrorCodes.SaleNotOpen, $"Tickets for this event are not on sale ({state}).");
        }
    }
}
