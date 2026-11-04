using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Sales;
using Gise.Core.Venues;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Gise.Api.Features.Sales;

/// <summary>Sets places aside for a buyer. Either every place asked for is taken, or none is.</summary>
internal sealed partial class HoldService(GiseDbContext db, QueueService queue, TimeProvider clock, IOptions<SalesOptions> options, ILogger<HoldService> logger)
{
    public async Task<HoldResponse> CreateAsync(Event @event, Guid userId, HoldRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var seats = (request.Seats ?? []).Select(s => (Section: s.Section?.Trim() ?? string.Empty, Row: s.Row?.Trim() ?? string.Empty, s.Number)).ToList();
        var standing = (request.Standing ?? []).Where(s => s.Quantity != 0).Select(s => (Section: s.Section?.Trim() ?? string.Empty, s.Quantity)).ToList();
        var wanted = seats.Count + standing.Sum(s => s.Quantity);

        EnsureOnSale(@event, now);
        Check(@event, seats, standing, wanted);
        await queue.EnsureAdmittedAsync(@event, userId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PlaceClaims.LockBuyerAsync(db, @event.Id, userId, ct);

        // One basket per buyer and event: choosing again replaces what they had.
        var previous = await db.Holds.Where(h => h.EventId == @event.Id && h.UserId == userId && h.Status == HoldStatus.Active).ToListAsync(ct);
        foreach (var old in previous)
        {
            old.Release();
            await PlaceClaims.ReleaseHeldAsync(db, old.Id, ct);
        }

        var owned = await db.Orders
            .Where(o => o.EventId == @event.Id && o.UserId == userId && (o.Status == OrderStatus.Paid || o.Status == OrderStatus.PendingPayment))
            .SumAsync(o => o.Lines.Count, ct);
        if (owned + wanted > @event.MaxTicketsPerBuyer)
        {
            throw new DomainException(ErrorCodes.TicketLimitReached, owned == 0
                ? $"One buyer may get at most {@event.MaxTicketsPerBuyer} tickets for this event."
                : $"One buyer may get at most {@event.MaxTicketsPerBuyer} tickets for this event and you already have {owned}.");
        }

        var hold = Hold.Create(@event.Id, userId, wanted, now, TimeSpan.FromMinutes(options.Value.HoldMinutes));
        db.Holds.Add(hold);
        await db.SaveChangesAsync(ct);

        try
        {
            if (seats.Count > 0)
            {
                var got = await PlaceClaims.ClaimSeatsAsync(db, @event.Id, hold.Id, seats, hold.ExpiresAt, now, ct);
                if (got.Count < seats.Count)
                {
                    await transaction.RollbackAsync(ct);
                    throw await TakenAsync(@event, seats, now, ct);
                }
            }

            foreach (var (section, quantity) in standing)
            {
                var got = await PlaceClaims.ClaimStandingAsync(db, @event.Id, hold.Id, section, quantity, hold.ExpiresAt, now, ct);
                if (got.Count < quantity)
                {
                    await transaction.RollbackAsync(ct);
                    var name = @event.Layout.First(s => s.Key == section).Name;
                    throw new DomainException(ErrorCodes.PlaceUnavailable, got.Count == 0
                        ? $"{name} is sold out."
                        : $"Only {got.Count} places are left in {name}.");
                }
            }
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.LockNotAvailable)
        {
            throw new DomainException(ErrorCodes.PlaceUnavailable, "Someone else is taking these places right now. Try again in a moment.");
        }

        await transaction.CommitAsync(ct);
        LogHeld(logger, hold.Id, @event.Id, wanted);
        return await ResponseAsync(@event, hold, ct);
    }

    public async Task ReleaseAsync(Hold hold, CancellationToken ct)
    {
        if (hold.Status != HoldStatus.Active)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        hold.Release();
        await PlaceClaims.ReleaseHeldAsync(db, hold.Id, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<HoldResponse> ResponseAsync(Event @event, Hold hold, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var places = hold.IsActive(now)
            ? await db.Places.AsNoTracking().Where(p => p.HoldId == hold.Id && p.Status == PlaceStatus.Held).ToListAsync(ct)
            : [];
        var lines = SalesMapping.Lines(@event, places).Select(SalesMapping.ToLine).ToList();
        var status = hold.Status == HoldStatus.Active && !hold.IsActive(now) ? HoldStatus.Expired : hold.Status;
        return new HoldResponse(hold.Id, hold.EventId, status, hold.ExpiresAt, lines, lines.Sum(l => l.Price), Money.Currency);
    }

    internal static void EnsureOnSale(Event @event, DateTimeOffset now)
    {
        var state = @event.SaleStateAt(now, false);
        if (state != SaleState.OnSale)
        {
            throw new DomainException(ErrorCodes.SaleNotOpen, state switch
            {
                SaleState.Upcoming => $"Sales open at {@event.SaleStartsAt:u}.",
                SaleState.Cancelled => "This event was cancelled.",
                SaleState.Ended => "This event has started; sales are closed.",
                _ => "Tickets for this event are not on sale.",
            });
        }
    }

    private static void Check(Event @event, List<(string Section, string Row, int Number)> seats, List<(string Section, int Quantity)> standing, int wanted)
    {
        if (wanted < 1)
        {
            throw Invalid("Choose at least one place.");
        }

        if (wanted > @event.MaxTicketsPerBuyer)
        {
            throw new DomainException(ErrorCodes.TicketLimitReached, $"One buyer may get at most {@event.MaxTicketsPerBuyer} tickets for this event.");
        }

        foreach (var (section, row, number) in seats)
        {
            var layout = @event.Layout.FirstOrDefault(s => s.Key == section);
            if (layout is not { Kind: SectionKind.Seated })
            {
                throw Invalid($"There is no seated section \"{section}\".");
            }

            var seatRow = layout.Rows.FirstOrDefault(r => r.Label == row);
            if (seatRow is null || number < 1 || number > seatRow.Seats)
            {
                throw Invalid($"{layout.Name} has no seat {number} in row {row}.");
            }
        }

        if (seats.Distinct().Count() != seats.Count)
        {
            throw Invalid("The same seat is in the list twice.");
        }

        foreach (var (section, quantity) in standing)
        {
            if (@event.Layout.FirstOrDefault(s => s.Key == section) is not { Kind: SectionKind.Standing })
            {
                throw Invalid($"There is no standing area \"{section}\".");
            }

            if (quantity < 0)
            {
                throw Invalid("Quantities are positive.");
            }
        }

        if (standing.Select(s => s.Section).Distinct().Count() != standing.Count)
        {
            throw Invalid("Each standing area is listed once.");
        }
    }

    // After a failed claim: say exactly which seats went, so the seat map can grey them out.
    private async Task<DomainException> TakenAsync(Event @event, List<(string Section, string Row, int Number)> seats, DateTimeOffset now, CancellationToken ct)
    {
        var sections = seats.Select(s => s.Section).Distinct().ToList();
        var places = await db.Places.AsNoTracking()
            .Where(p => p.EventId == @event.Id && sections.Contains(p.SectionKey) && !(p.Status == PlaceStatus.Available || (p.Status == PlaceStatus.Held && p.HeldUntil <= now)))
            .Select(p => new { p.SectionKey, p.Row, p.Number })
            .ToListAsync(ct);
        var taken = seats.Where(s => places.Any(p => p.SectionKey == s.Section && p.Row == s.Row && p.Number == s.Number)).ToList();
        var names = taken.Count > 0 ? taken : seats;
        return new DomainException(ErrorCodes.PlaceUnavailable,
            $"Taken a moment ago: {string.Join(", ", names.Select(s => $"{@event.Layout.First(l => l.Key == s.Section).Name} {s.Row}{s.Number}"))}. Choose other seats.");
    }

    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationFailed, message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hold {HoldId} for event {Event}: {Places} places")]
    private static partial void LogHeld(ILogger logger, Guid holdId, Guid @event, int places);
}
