using Gise.Core.Events;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Events;

public sealed record InventoryCounts(int Available, int Held, int Sold);

/// <summary>How many places are free, in someone's basket, or sold. A hold that ran out counts as free.</summary>
internal static class Inventory
{
    public static async Task<Dictionary<Guid, InventoryCounts>> CountAsync(GiseDbContext db, IReadOnlyCollection<Guid> eventIds, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Places.AsNoTracking()
            .Where(p => eventIds.Contains(p.EventId))
            .GroupBy(p => p.EventId)
            .Select(g => new
            {
                g.Key,
                Sold = g.Count(p => p.Status == PlaceStatus.Sold),
                Held = g.Count(p => p.Status == PlaceStatus.Held && p.HeldUntil > now),
                Total = g.Count(),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => new InventoryCounts(r.Total - r.Sold - r.Held, r.Held, r.Sold));
    }

    public static async Task<InventoryCounts> CountAsync(GiseDbContext db, Guid eventId, DateTimeOffset now, CancellationToken ct) =>
        (await CountAsync(db, [eventId], now, ct)).GetValueOrDefault(eventId) ?? new InventoryCounts(0, 0, 0);
}
