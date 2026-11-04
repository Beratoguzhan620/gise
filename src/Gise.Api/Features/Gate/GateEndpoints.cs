using System.Security.Claims;
using Gise.Api.Common;
using Gise.Api.Features.Events;
using Gise.Api.Features.Organizations;
using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Core.Tickets;
using Gise.Infrastructure.Persistence;
using Gise.Infrastructure.Tickets;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Gate;

internal static class GateEndpoints
{
    public static IEndpointRouteBuilder MapGateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/events/{eventId:guid}").WithTags("Gate").RequireAuthorization();
        group.MapGet("/gate", Package).WithSummary("Everything a gate device needs to check tickets offline (staff)")
            .WithDescription("""
                Ticket codes are `GS1.<payload>.<signature>` (base64url). The payload is 37 bytes: version `1`, the first four
                bytes of the key id, the ticket id and the event id (16 bytes each, big-endian). The signature is ECDSA P-256
                with SHA-256 over the payload, 64 raw bytes. `publicKey` is SubjectPublicKeyInfo in base64, ready for
                WebCrypto's `importKey("spki", …)`.
                """);
        group.MapPost("/check-ins", Scan).WithSummary("Check tickets in: one scan, or a device's offline backlog (staff)")
            .WithDescription("""
                Each scan carries an id made on the device, so sending the same backlog twice is harmless. The earliest scan of a
                ticket lets it in; later ones come back as `AlreadyUsed` with when and where it was first used.
                """);
        group.MapGet("/check-ins", Summary).WithSummary("Who is in, by gate, and tickets used twice (staff)");
        return app;
    }

    internal static GateTicket ToGate(Ticket t) => new(t.Id, t.Label, t.CategoryName, t.HolderName, t.Status, t.CheckedInAt, t.CheckedInGate);

    private static async Task<Ok<GatePackage>> Package(
        Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, TicketCodes codes, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var @event = await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
        var tickets = await db.Tickets.AsNoTracking().Where(t => t.EventId == eventId)
            .OrderBy(t => t.SectionName).ThenBy(t => t.Row).ThenBy(t => t.Number).ToListAsync(ct);
        return TypedResults.Ok(new GatePackage(
            @event.Id, @event.Title, @event.StartsAt, @event.VenueName, codes.KeyId, Convert.ToBase64String(codes.PublicKey),
            tickets.Select(ToGate).ToList(), clock.GetUtcNow()));
    }

    private static async Task<Ok<List<ScanResult>>> Scan(
        Guid organizationId, Guid eventId, ScanBatch batch, ClaimsPrincipal user, GiseDbContext db, CheckInService checkIns, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var @event = await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
        if (@event.Status != EventStatus.Published)
        {
            throw new Gise.Core.DomainException(Gise.Core.ErrorCodes.InvalidState, "Only an event on sale lets people in.");
        }

        return TypedResults.Ok(await checkIns.ProcessAsync(eventId, user.UserId(), batch.Scans ?? [], ct));
    }

    private static async Task<Ok<CheckInSummary>> Summary(Guid organizationId, Guid eventId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        await EventEndpoints.FindAsync(db, organizationId, eventId, ct);
        var tickets = db.Tickets.AsNoTracking().Where(t => t.EventId == eventId && t.Status == TicketStatus.Valid);
        var total = await tickets.CountAsync(ct);
        var gates = await tickets.Where(t => t.CheckedInAt != null).GroupBy(t => t.CheckedInGate!)
            .Select(g => new GateCount(g.Key, g.Count())).ToListAsync(ct);
        var repeats = await (from c in db.CheckIns.AsNoTracking()
                             join t in db.Tickets on c.TicketId equals t.Id
                             where c.EventId == eventId && c.Outcome == CheckInOutcome.AlreadyUsed
                             orderby c.ScannedAt descending
                             select new { c, t })
            .Take(50).ToListAsync(ct);
        var recent = await (from c in db.CheckIns.AsNoTracking()
                            join t in db.Tickets on c.TicketId equals t.Id into matched
                            from t in matched.DefaultIfEmpty()
                            where c.EventId == eventId
                            orderby c.ScannedAt descending
                            select new { c, t })
            .Take(20).ToListAsync(ct);
        var rejected = await db.CheckIns.CountAsync(c => c.EventId == eventId && c.Outcome != CheckInOutcome.Admitted && c.Outcome != CheckInOutcome.AlreadyUsed, ct);

        var admitted = gates.Sum(g => g.Admitted);
        return TypedResults.Ok(new CheckInSummary(
            eventId,
            total,
            admitted,
            total - admitted,
            gates.OrderByDescending(g => g.Admitted).ToList(),
            repeats.Select(r => new RepeatScan(r.t.Id, r.t.Label, r.t.HolderName, r.t.CheckedInAt ?? r.c.ScannedAt, r.t.CheckedInGate, r.c.ScannedAt, r.c.Gate, r.c.Device)).ToList(),
            recent.Select(r => new RecentScan(r.c.Id, r.c.Outcome, r.t?.Label, r.c.Gate, r.c.Device, r.c.ScannedAt)).ToList(),
            rejected));
    }
}
