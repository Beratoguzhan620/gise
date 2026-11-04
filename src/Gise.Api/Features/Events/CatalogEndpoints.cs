using System.Text;
using Gise.Api.Common;
using Gise.Api.Features.Venues;
using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Venues;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Gise.Api.Features.Events;

public sealed record EventSummary(
    Guid Id, string Title, string Organizer, string VenueName, string City, DateTimeOffset StartsAt, DateTimeOffset SaleStartsAt,
    SaleState SaleState, decimal PriceFrom, string Currency, int Available, int Capacity, bool WaitingRoom);

public sealed record PublicSection(string Key, string Name, SectionKind Kind, Guid CategoryId, int Places, IReadOnlyList<RowResponse> Rows, int Capacity);

public sealed record EventDetail(
    Guid Id,
    string Title,
    string Description,
    string Organizer,
    string VenueName,
    string City,
    string Address,
    DateTimeOffset StartsAt,
    DateTimeOffset SaleStartsAt,
    SaleState SaleState,
    int MaxTicketsPerBuyer,
    DateTimeOffset? RefundDeadline,
    bool WaitingRoom,
    string Currency,
    IReadOnlyList<CategoryResponse> Categories,
    IReadOnlyList<PublicSection> Sections,
    int Available,
    int Capacity);

/// <summary>Seats as one string per row, one character per seat from seat 1: <c>1</c> free, <c>0</c> taken.</summary>
public sealed record RowAvailability(string Label, string Seats);

public sealed record SectionAvailability(string Key, SectionKind Kind, int Available, int Places, IReadOnlyList<RowAvailability> Rows);

public sealed record AvailabilityResponse(Guid EventId, SaleState SaleState, int Available, IReadOnlyList<SectionAvailability> Sections, DateTimeOffset CheckedAt);

/// <summary>What anyone can see without an account: events on sale and what is left of them.</summary>
internal static class CatalogEndpoints
{
    // Seat maps are asked for by everyone at once when a sale opens; a second-old answer is fine, a database
    // query per visitor is not. Taking a seat always checks the database, never this cache.
    private static readonly TimeSpan AvailabilityFreshness = TimeSpan.FromSeconds(1);

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events").WithTags("Events");
        group.MapGet("/", List).WithSummary("Upcoming events")
            .WithDescription("`q` searches titles, venues and cities. Ordered by start time.");
        group.MapGet("/{eventId:guid}", Get).WithSummary("An event, its prices and floor plan");
        group.MapGet("/{eventId:guid}/availability", Availability).WithSummary("What is still free, seat by seat")
            .WithDescription("Each row of a seated section is a string with one character per seat from seat 1: `1` free, `0` taken. At most a second old.");
        return app;
    }

    private static async Task<Ok<Page<EventSummary>>> List(
        GiseDbContext db, TimeProvider clock, CancellationToken ct, string? q = null, string? city = null, int? page = null, int? pageSize = null)
    {
        var (number, size) = Paging.Clamp(page, pageSize);
        var now = clock.GetUtcNow();
        var query = db.Events.AsNoTracking().Where(e => e.Status == EventStatus.Published && e.StartsAt > now);
        if (!string.IsNullOrWhiteSpace(city))
        {
            var c = city.Trim();
            query = query.Where(e => EF.Functions.ILike(e.City, Like(c)));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{Like(q.Trim())}%";
            query = query.Where(e => EF.Functions.ILike(e.Title, pattern) || EF.Functions.ILike(e.VenueName, pattern) || EF.Functions.ILike(e.City, pattern));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(e => e.StartsAt).ThenBy(e => e.Id).Skip((number - 1) * size).Take(size)
            .Select(e => new
            {
                Event = e,
                Organizer = db.Organizations.Where(o => o.Id == e.OrganizationId).Select(o => o.Name).Single(),
            })
            .ToListAsync(ct);
        var counts = await Inventory.CountAsync(db, rows.Select(r => r.Event.Id).ToList(), now, ct);
        var items = rows.Select(r =>
        {
            var e = r.Event;
            var available = counts.GetValueOrDefault(e.Id)?.Available ?? 0;
            return new EventSummary(e.Id, e.Title, r.Organizer, e.VenueName, e.City, e.StartsAt, e.SaleStartsAt, e.SaleStateAt(now, available == 0),
                Money.FromMinor(e.Categories.Min(c => c.PriceMinor)), Money.Currency, available, e.Capacity, e.WaitingRoom);
        }).ToList();
        return TypedResults.Ok(new Page<EventSummary>(items, total, number, size));
    }

    private static async Task<Ok<EventDetail>> Get(Guid eventId, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var @event = await FindPublicAsync(db, eventId, ct);
        var organizer = await db.Organizations.Where(o => o.Id == @event.OrganizationId).Select(o => o.Name).SingleAsync(ct);
        var now = clock.GetUtcNow();
        var available = (await Inventory.CountAsync(db, eventId, now, ct)).Available;
        var sections = @event.Layout.Select(s => new PublicSection(
            s.Key, s.Name, s.Kind, @event.CategoryOf(s.Key).Id, s.Places, s.Rows.Select(r => new RowResponse(r.Label, r.Seats)).ToList(), s.Capacity)).ToList();
        return TypedResults.Ok(new EventDetail(
            @event.Id, @event.Title, @event.Description, organizer, @event.VenueName, @event.City, @event.Address, @event.StartsAt, @event.SaleStartsAt,
            @event.SaleStateAt(now, available == 0), @event.MaxTicketsPerBuyer, @event.RefundDeadline, @event.WaitingRoom, Money.Currency,
            OrganizerEventResponse.CategoriesOf(@event), sections, available, @event.Capacity));
    }

    private static async Task<Ok<AvailabilityResponse>> Availability(Guid eventId, GiseDbContext db, TimeProvider clock, IMemoryCache cache, CancellationToken ct)
    {
        var response = await cache.GetOrCreateAsync(("availability", eventId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AvailabilityFreshness;
            return await ReadAvailabilityAsync(db, eventId, clock.GetUtcNow(), ct);
        });
        return TypedResults.Ok(response!);
    }

    internal static async Task<AvailabilityResponse> ReadAvailabilityAsync(GiseDbContext db, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var @event = await FindPublicAsync(db, eventId, ct);
        var places = await db.Places.AsNoTracking().Where(p => p.EventId == eventId)
            .Select(p => new { p.SectionKey, p.Row, p.Number, Free = p.Status == PlaceStatus.Available || (p.Status == PlaceStatus.Held && p.HeldUntil <= now) })
            .ToListAsync(ct);
        var bySection = places.ToLookup(p => p.SectionKey, StringComparer.Ordinal);

        var sections = new List<SectionAvailability>();
        foreach (var section in @event.Layout)
        {
            var mine = bySection[section.Key].ToList();
            var free = mine.Count(p => p.Free);
            var rows = new List<RowAvailability>();
            if (section.Kind == SectionKind.Seated)
            {
                var byRow = mine.ToLookup(p => p.Row, StringComparer.Ordinal);
                foreach (var row in section.Rows)
                {
                    var seats = new StringBuilder(new string('0', row.Seats));
                    foreach (var seat in byRow[row.Label].Where(p => p.Free && p.Number <= row.Seats))
                    {
                        seats[seat.Number - 1] = '1';
                    }

                    rows.Add(new RowAvailability(row.Label, seats.ToString()));
                }
            }

            sections.Add(new SectionAvailability(section.Key, section.Kind, free, section.Places, rows));
        }

        var available = sections.Sum(s => s.Available);
        return new AvailabilityResponse(eventId, @event.SaleStateAt(now, available == 0), available, sections, now);
    }

    internal static async Task<Event> FindPublicAsync(GiseDbContext db, Guid eventId, CancellationToken ct) =>
        await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId && e.Status != EventStatus.Draft, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no event with this id.");

    private static string Like(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
