using Microsoft.EntityFrameworkCore;

namespace Gise.Infrastructure.Persistence;

/// <summary>
/// Every change to who has a place goes through these statements. Each one only touches places in the state it
/// expects and reports how many it changed, so callers can tell when someone else got there first and roll back.
/// A place whose hold ran out counts as free without anyone having to clean it up first.
/// </summary>
public static class PlaceClaims
{
    /// <summary>Takes these exact seats for a hold; returns the ids it got. Locks in id order, so two buyers never deadlock.</summary>
    public static Task<List<Guid>> ClaimSeatsAsync(
        GiseDbContext db, Guid eventId, Guid holdId, IReadOnlyList<(string Section, string Row, int Number)> seats, DateTimeOffset heldUntil, DateTimeOffset now,
        CancellationToken ct)
    {
        var sections = seats.Select(s => s.Section).ToArray();
        var rows = seats.Select(s => s.Row).ToArray();
        var numbers = seats.Select(s => s.Number).ToArray();
        return db.Database.SqlQuery<Guid>($"""
            WITH wanted AS (
                SELECT p.id
                FROM places p
                JOIN unnest({sections}::text[], {rows}::text[], {numbers}::int[]) AS s(section_key, row, number)
                  ON p.section_key = s.section_key AND p.row = s.row AND p.number = s.number
                WHERE p.event_id = {eventId}
                ORDER BY p.id
                FOR UPDATE OF p
            )
            UPDATE places p
            SET status = 'Held', hold_id = {holdId}, held_until = {heldUntil}
            FROM wanted
            WHERE p.id = wanted.id
              AND (p.status = 'Available' OR (p.status = 'Held' AND p.held_until <= {now}))
            RETURNING p.id AS "Value"
            """).ToListAsync(ct);
    }

    /// <summary>Takes up to <paramref name="quantity"/> spots in a standing area, skipping any another buyer is taking right now.</summary>
    public static Task<List<Guid>> ClaimStandingAsync(
        GiseDbContext db, Guid eventId, Guid holdId, string section, int quantity, DateTimeOffset heldUntil, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"""
            UPDATE places
            SET status = 'Held', hold_id = {holdId}, held_until = {heldUntil}
            WHERE id IN (
                SELECT id FROM places
                WHERE event_id = {eventId} AND section_key = {section}
                  AND (status = 'Available' OR (status = 'Held' AND held_until <= {now}))
                ORDER BY number
                LIMIT {quantity}
                FOR UPDATE SKIP LOCKED
            )
            RETURNING id AS "Value"
            """).ToListAsync(ct);

    /// <summary>Keeps a hold's places until <paramref name="until"/>; returns how many it still had.</summary>
    public static Task<int> ExtendAsync(GiseDbContext db, Guid holdId, DateTimeOffset until, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"UPDATE places SET held_until = {until} WHERE hold_id = {holdId} AND status = 'Held'", ct);

    /// <summary>Puts a hold's places back on sale.</summary>
    public static Task<int> ReleaseHeldAsync(GiseDbContext db, Guid holdId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"UPDATE places SET status = 'Available', hold_id = NULL, held_until = NULL WHERE hold_id = {holdId} AND status = 'Held'", ct);

    /// <summary>Sells the places still held for a paid hold; returns how many.</summary>
    public static Task<int> SellAsync(GiseDbContext db, Guid holdId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"UPDATE places SET status = 'Sold', held_until = NULL WHERE hold_id = {holdId} AND status = 'Held'", ct);

    /// <summary>
    /// For a payment that arrived after its places were let go: sells the same places again if every one of them is
    /// still free (or still this hold's). Returns how many it got; the caller rolls back unless that is all of them.
    /// </summary>
    public static Task<int> ReclaimAsync(GiseDbContext db, Guid holdId, Guid[] placeIds, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE places
            SET status = 'Sold', hold_id = {holdId}, held_until = NULL
            WHERE id = ANY({placeIds})
              AND (status = 'Available' OR (status = 'Held' AND (hold_id = {holdId} OR held_until <= {now})))
            """, ct);

    /// <summary>A refunded order's places go back on sale.</summary>
    public static Task<int> ReleaseSoldAsync(GiseDbContext db, Guid holdId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"UPDATE places SET status = 'Available', hold_id = NULL, held_until = NULL WHERE hold_id = {holdId} AND status = 'Sold'", ct);

    /// <summary>Serializes one buyer's requests for one event, so two tabs cannot both pass the ticket limit.</summary>
    public static Task LockBuyerAsync(GiseDbContext db, Guid eventId, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({eventId.ToString() + userId}, 0))", ct);
}
