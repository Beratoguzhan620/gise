using System.Security.Claims;
using Gise.Api.Common;
using Gise.Core;
using Gise.Core.Events;
using Gise.Core.Sales;
using Gise.Core.Tickets;
using Gise.Infrastructure.Persistence;
using Gise.Infrastructure.Tickets;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Tickets;

public sealed record MyTicket(
    Guid Id, Guid OrderId, string OrderNumber, Guid EventId, string EventTitle, string VenueName, string City, DateTimeOffset StartsAt,
    string Label, string Section, string? Row, int Number, string Category, decimal Price, string Holder, TicketStatus Status, string? Code,
    DateTimeOffset? CheckedInAt);

internal static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Tickets").RequireAuthorization();
        group.MapGet("/tickets", List).WithSummary("Your tickets, soonest event first")
            .WithDescription("`code` is the text in the QR code; it is only given for valid tickets. `past=true` lists events that have started.");
        group.MapGet("/tickets/{ticketId:guid}/qr.png", Qr).WithSummary("A ticket's QR code as an image");
        group.MapGet("/orders/{orderId:guid}/tickets.pdf", Pdf).WithSummary("A paid order's tickets as a PDF, one page each");
        return app;
    }

    private static async Task<Ok<List<MyTicket>>> List(ClaimsPrincipal user, GiseDbContext db, TicketCodes codes, TimeProvider clock, CancellationToken ct, bool past = false)
    {
        var userId = user.UserId();
        var now = clock.GetUtcNow();
        var rows = await (from t in db.Tickets.AsNoTracking()
                          join o in db.Orders on t.OrderId equals o.Id
                          join e in db.Events on t.EventId equals e.Id
                          where t.UserId == userId && (past ? e.StartsAt <= now : e.StartsAt > now)
                          orderby e.StartsAt, t.OrderId, t.SectionName, t.Row, t.Number
                          select new { Ticket = t, o.Number, e.Title, e.VenueName, e.City, e.StartsAt })
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(r => new MyTicket(
            r.Ticket.Id, r.Ticket.OrderId, r.Number, r.Ticket.EventId, r.Title, r.VenueName, r.City, r.StartsAt, r.Ticket.Label, r.Ticket.SectionName,
            r.Ticket.Row, r.Ticket.Number, r.Ticket.CategoryName, Money.FromMinor(r.Ticket.PriceMinor), r.Ticket.HolderName, r.Ticket.Status,
            r.Ticket.Status == TicketStatus.Valid ? codes.Sign(r.Ticket.Id, r.Ticket.EventId) : null, r.Ticket.CheckedInAt)).ToList());
    }

    private static async Task<FileContentHttpResult> Qr(Guid ticketId, ClaimsPrincipal user, GiseDbContext db, TicketCodes codes, CancellationToken ct)
    {
        var userId = user.UserId();
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId && t.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such ticket among yours.");
        if (ticket.Status != TicketStatus.Valid)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This ticket was voided and has no code any more.");
        }

        return TypedResults.File(TicketQr.Png(codes.Sign(ticket.Id, ticket.EventId)), "image/png");
    }

    private static async Task<FileContentHttpResult> Pdf(Guid orderId, ClaimsPrincipal user, GiseDbContext db, EmailComposer composer, CancellationToken ct)
    {
        var userId = user.UserId();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no such order among yours.");
        if (order.Status != OrderStatus.Paid)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only a paid order has tickets.");
        }

        var @event = await db.Events.AsNoTracking().SingleAsync(e => e.Id == order.EventId, ct);
        return TypedResults.File(await composer.PdfAsync(order, @event, ct), "application/pdf", $"gise-{order.Number}.pdf");
    }
}
