using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Gise.Core.Events;
using Gise.Core.Sales;
using Gise.Core.Tickets;
using Gise.Infrastructure.Mail;
using Gise.Infrastructure.Persistence;
using Gise.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Tickets;

/// <summary>Writes the emails from the order as it is when they are sent.</summary>
internal sealed class EmailComposer(GiseDbContext db, TicketCodes codes, TicketTime time)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Escapes what HTML needs escaped and leaves Turkish letters readable in the source.
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public async Task<MailToSend> ComposeAsync(OutboxEmail email, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == email.OrderId, ct);
        var @event = await db.Events.AsNoTracking().SingleAsync(e => e.Id == order.EventId, ct);
        var when = time.Local(@event.StartsAt).ToString("dddd, d MMMM yyyy · HH:mm", CultureInfo.InvariantCulture);
        return email.Kind switch
        {
            EmailKind.Tickets => await TicketsAsync(email, order, @event, when, ct),
            _ => Refund(email, order, @event, when),
        };
    }

    public async Task<byte[]> PdfAsync(Order order, Event @event, CancellationToken ct)
    {
        var tickets = await db.Tickets.AsNoTracking().Where(t => t.OrderId == order.Id && t.Status == TicketStatus.Valid).ToListAsync(ct);
        var ordered = tickets.OrderBy(t => order.Lines.FindIndex(l => l.PlaceId == t.PlaceId)).ToList();
        return TicketPdf.Render(new TicketDocument(
            @event.Title, time.Local(@event.StartsAt), @event.VenueName, @event.City, @event.Address, order.Number,
            ordered.Select(t => new PrintedTicket(t.Id, codes.Sign(t.Id, t.EventId), t.Label, t.CategoryName, Money.FromMinor(t.PriceMinor), t.HolderName)).ToList()));
    }

    private async Task<MailToSend> TicketsAsync(OutboxEmail email, Order order, Event @event, string when, CancellationToken ct)
    {
        var lines = order.Lines.Select(l => (l.Row is null ? l.SectionName : $"{l.SectionName} · Row {l.Row} · Seat {l.Number}", $"{l.CategoryName}, {Price(l.PriceMinor)}")).ToList();
        var subject = $"Your tickets: {@event.Title}";
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"{@event.Title}")
            .AppendLine(CultureInfo.InvariantCulture, $"{when} · {@event.VenueName}, {@event.City}")
            .AppendLine()
            .AppendJoin(Environment.NewLine, lines.Select(l => $"- {l.Item1} ({l.Item2})"))
            .AppendLine()
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Order {order.Number}, {Price(order.AmountMinor)} paid.")
            .AppendLine("Your tickets are attached as a PDF and are also in the app under My tickets. Each code lets one person in, once.")
            .ToString();
        var rows = string.Concat(lines.Select(l =>
            $"""<tr><td style="padding:10px 0;border-top:1px solid #E2DFD8">{E(l.Item1)}</td><td style="padding:10px 0;border-top:1px solid #E2DFD8;text-align:right;color:#6B6862">{E(l.Item2)}</td></tr>"""));
        var html = Layout(subject, $"""
            <h1 style="font-size:20px;font-weight:600;margin:0 0 4px">{E(@event.Title)}</h1>
            <p style="color:#6B6862;margin:0 0 20px">{E(when)} · {E(@event.VenueName)}, {E(@event.City)}</p>
            <table role="presentation" style="width:100%;border-collapse:collapse;margin:0 0 20px">
              {rows}
            </table>
            <p style="margin:0 0 8px">Order <strong>{E(order.Number)}</strong>, {E(Price(order.AmountMinor))} paid.</p>
            <p style="color:#6B6862;margin:0">Your tickets are attached as a PDF and are also in the app under My tickets. Each code lets one person in, once.</p>
            """);
        var pdf = await PdfAsync(order, @event, ct);
        return new MailToSend(email.To, subject, text, html, [new MailAttachment($"gise-{order.Number}.pdf", "application/pdf", pdf)]);
    }

    private static MailToSend Refund(OutboxEmail email, Order order, Event @event, string when)
    {
        var reason = order.RefundReason switch
        {
            RefundReason.EventCancelled => "The organizer cancelled the event.",
            RefundReason.LatePayment => "Your payment arrived after the places had gone to someone else.",
            RefundReason.Organizer => "The organizer refunded your order.",
            _ => "You asked for a refund.",
        };
        var subject = $"Refund for order {order.Number}";
        var text = $"{@event.Title}, {when}{Environment.NewLine}{reason}{Environment.NewLine}{Price(order.AmountMinor)} goes back to the card you paid with. The tickets of this order no longer work.";
        var html = Layout(subject, $"""
            <h1 style="font-size:20px;font-weight:600;margin:0 0 4px">Refund for order {E(order.Number)}</h1>
            <p style="color:#6B6862;margin:0 0 20px">{E(@event.Title)} · {E(when)}</p>
            <p style="margin:0 0 8px">{E(reason)}</p>
            <p style="margin:0">{E(Price(order.AmountMinor))} goes back to the card you paid with. The tickets of this order no longer work.</p>
            """);
        return new MailToSend(email.To, subject, text, html, []);
    }

    private static string Layout(string title, string content) => $"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>{E(title)}</title></head>
        <body style="margin:0;padding:24px 12px;background:#F7F6F3;font-family:'IBM Plex Sans',Arial,sans-serif;color:#1A1A1A;font-size:15px;line-height:1.5">
          <div style="max-width:560px;margin:0 auto;background:#FFFFFF;border:1px solid #E2DFD8;border-radius:10px;overflow:hidden">
            <div style="background:#8E2F3C;color:#FFFFFF;padding:14px 24px;font-weight:600;font-size:17px">Gişe</div>
            <div style="padding:24px">{content}</div>
          </div>
        </body></html>
        """;

    private static string Price(long minor) => $"{Money.FromMinor(minor).ToString("N2", Turkish)} TRY";

    private static string E(string value) => Html.Encode(value);
}
