using System.Security.Claims;
using Gise.Api.Common;
using Gise.Api.Features.Organizations;
using Gise.Core;
using Gise.Core.Organizations;
using Gise.Core.Venues;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Venues;

public sealed record RowRequest(string Label, int Seats);

public sealed record SectionRequest(string Key, string Name, SectionKind Kind, List<RowRequest>? Rows = null, int Capacity = 0);

public sealed record VenueRequest(string Name, string City, string? Address, List<SectionRequest> Sections);

public sealed record RowResponse(string Label, int Seats);

public sealed record SectionResponse(string Key, string Name, SectionKind Kind, int Places, IReadOnlyList<RowResponse> Rows, int Capacity);

public sealed record VenueResponse(
    Guid Id, string Name, string City, string Address, int Capacity, IReadOnlyList<SectionResponse> Sections, int Events, DateTimeOffset CreatedAt);

internal static class VenueEndpoints
{
    public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/venues").WithTags("Venues").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The organizer's venues");
        group.MapPost("/", Create).WithSummary("Add a venue with its floor plan (managers)")
            .WithDescription("""
                A floor plan is a list of sections in drawing order, nearest the stage first. A `Seated` section has rows
                (`label`, `seats`); a `Standing` section has a `capacity`. Up to 50,000 places in all.
                """);
        group.MapGet("/{venueId:guid}", Get).WithSummary("One venue");
        group.MapPut("/{venueId:guid}", Update).WithSummary("Change a venue (managers)")
            .WithDescription("Events that are already on sale keep the floor plan they were published with.");
        group.MapDelete("/{venueId:guid}", Delete).WithSummary("Delete a venue no event uses (managers)");
        return app;
    }

    private static async Task<Ok<List<VenueResponse>>> List(Guid organizationId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var venues = await db.Venues.AsNoTracking().Where(v => v.OrganizationId == organizationId).OrderBy(v => v.Name).ToListAsync(ct);
        var events = await EventCounts(db, organizationId, ct);
        return TypedResults.Ok(venues.Select(v => ToResponse(v, events.GetValueOrDefault(v.Id))).ToList());
    }

    private static async Task<Created<VenueResponse>> Create(
        Guid organizationId, VenueRequest request, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var venue = Venue.Create(organizationId, request.Name, request.City, request.Address, ToSections(request.Sections), clock.GetUtcNow());
        db.Venues.Add(venue);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/organizations/{organizationId}/venues/{venue.Id}", ToResponse(venue, 0));
    }

    private static async Task<Ok<VenueResponse>> Get(Guid organizationId, Guid venueId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var venue = await FindAsync(db, organizationId, venueId, ct);
        return TypedResults.Ok(ToResponse(venue, await db.Events.CountAsync(e => e.VenueId == venueId, ct)));
    }

    private static async Task<Ok<VenueResponse>> Update(
        Guid organizationId, Guid venueId, VenueRequest request, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var venue = await FindAsync(db, organizationId, venueId, ct);
        venue.Update(request.Name, request.City, request.Address, ToSections(request.Sections));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(venue, await db.Events.CountAsync(e => e.VenueId == venueId, ct)));
    }

    private static async Task<NoContent> Delete(Guid organizationId, Guid venueId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Manager, ct);
        var venue = await FindAsync(db, organizationId, venueId, ct);
        if (await db.Events.AnyAsync(e => e.VenueId == venueId, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "Events use this venue. Delete or move them first.");
        }

        db.Venues.Remove(venue);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    internal static async Task<Venue> FindAsync(GiseDbContext db, Guid organizationId, Guid venueId, CancellationToken ct) =>
        await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId && v.OrganizationId == organizationId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no venue with this id among the organizer's.");

    internal static IReadOnlyList<SectionResponse> ToSections(IEnumerable<Section> sections) =>
        sections.Select(s => new SectionResponse(
            s.Key, s.Name, s.Kind, s.Places, s.Rows.Select(r => new RowResponse(r.Label, r.Seats)).ToList(), s.Capacity)).ToList();

    private static List<Section> ToSections(List<SectionRequest>? sections) =>
        (sections ?? []).Select(s => new Section
        {
            Key = s.Key,
            Name = s.Name,
            Kind = s.Kind,
            Capacity = s.Capacity,
            Rows = (s.Rows ?? []).Select(r => new SeatRow { Label = r.Label, Seats = r.Seats }).ToList(),
        }).ToList();

    private static VenueResponse ToResponse(Venue v, int events) =>
        new(v.Id, v.Name, v.City, v.Address, v.Capacity, ToSections(v.Sections), events, v.CreatedAt);

    private static Task<Dictionary<Guid, int>> EventCounts(GiseDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Events.Where(e => e.OrganizationId == organizationId).GroupBy(e => e.VenueId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}
