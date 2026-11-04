using System.Security.Claims;
using Gise.Api.Features.Organizations;
using Gise.Api.Features.Venues;
using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Events;

/// <summary>What organizers do with their events: prepare drafts, put them on sale, follow sales.</summary>
internal static partial class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/events").WithTags("Events (organizer)").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The organizer's events, drafts included");
        group.MapPost("/", Create).WithSummary("Prepare an event as a draft (managers)")
            .WithDescription("""
                `categories` are the price levels, `pricing` gives every section of the venue one of them by name.
                Buyers may refund until `refundCutoffHours` before the start (`null`: only the organizer refunds).
                With `waitingRoom` on, buyers queue and are let in at `admitPerMinute`.
                """);
        group.MapGet("/{eventId:guid}", Get).WithSummary("One event with its sales so far");
        group.MapPut("/{eventId:guid}", Update).WithSummary("Change a draft (managers)");
        group.MapDelete("/{eventId:guid}", Delete).WithSummary("Delete a draft (managers)");
        group.MapPost("/{eventId:guid}/publish", Publish).WithSummary("Put a draft on sale (managers)")
            .WithDescription("Copies the venue's floor plan and creates one place per seat and standing spot. The event becomes public.");
        return app;
    }

    private static async Task<Ok<List<OrganizerEventResponse>>> List(Guid organizationId, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var events = await db.Events.AsNoTracking().Where(e => e.OrganizationId == organizationId).OrderBy(e => e.StartsAt).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var counts = await Inventory.CountAsync(db, events.Select(e => e.Id).ToList(), now, ct);
        return TypedResults.Ok(events.Select(e => OrganizerEventResponse.From(e, counts.GetValueOrDefault(e.Id) ?? Empty(e), now)).ToList());
    }

    private static async Task<Created<OrganizerEventResponse>> Create(
        Guid organizationId, EventRequest request, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var venue = await VenueEndpoints.FindAsync(db, organizationId, request.VenueId, ct);
        var now = clock.GetUtcNow();
        var @event = Event.CreateDraft(organizationId, venue, request.ToDetails(), now);
        db.Events.Add(@event);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/organizations/{organizationId}/events/{@event.Id}", OrganizerEventResponse.From(@event, Empty(@event), now));
    }

    private static async Task<Ok<OrganizerEventResponse>> Get(Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var @event = await FindAsync(db, organizationId, eventId, ct);
        return TypedResults.Ok(await ResponseAsync(db, @event, clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<OrganizerEventResponse>> Update(
        Guid organizationId, Guid eventId, EventRequest request, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var @event = await FindAsync(db, organizationId, eventId, ct);
        var venue = await VenueEndpoints.FindAsync(db, organizationId, request.VenueId, ct);
        var now = clock.GetUtcNow();
        @event.Update(venue, request.ToDetails(), now);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(OrganizerEventResponse.From(@event, Empty(@event), now));
    }

    private static async Task<NoContent> Delete(Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var @event = await FindAsync(db, organizationId, eventId, ct);
        @event.EnsureDraft("deleted");
        db.Events.Remove(@event);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<OrganizerEventResponse>> Publish(
        Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Two managers pressing publish at once: the second waits here, then finds the event already published.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM events WHERE id = {eventId} AND organization_id = {organizationId} FOR UPDATE", ct);
        var @event = await FindAsync(db, organizationId, eventId, ct);
        var venue = await VenueEndpoints.FindAsync(db, organizationId, @event.VenueId, ct);
        var now = clock.GetUtcNow();
        var places = @event.Publish(venue, now);
        await db.SaveChangesAsync(ct);
        await PlaceWriter.WriteAsync(db, places, ct);
        await transaction.CommitAsync(ct);

        LogPublished(loggers.CreateLogger(typeof(EventEndpoints)), @event.Id, places.Count);
        return TypedResults.Ok(OrganizerEventResponse.From(@event, new InventoryCounts(places.Count, 0, 0), now));
    }

    internal static async Task<Event> FindAsync(GiseDbContext db, Guid organizationId, Guid eventId, CancellationToken ct) =>
        await db.Events.SingleOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == organizationId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no event with this id among the organizer's.");

    internal static async Task<OrganizerEventResponse> ResponseAsync(GiseDbContext db, Event @event, DateTimeOffset now, CancellationToken ct) =>
        OrganizerEventResponse.From(@event, @event.Status == EventStatus.Draft ? Empty(@event) : await Inventory.CountAsync(db, @event.Id, now, ct), now);

    // A draft has no places yet; what it would sell is the venue's capacity.
    private static InventoryCounts Empty(Event e) => new(e.Status == EventStatus.Draft ? e.Capacity : 0, 0, 0);

    [LoggerMessage(Level = LogLevel.Information, Message = "Event {EventId} published with {Places} places")]
    private static partial void LogPublished(ILogger logger, Guid eventId, int places);
}
