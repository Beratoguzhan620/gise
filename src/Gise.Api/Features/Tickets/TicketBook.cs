using Gise.Core.Sales;
using Gise.Core.Tickets;
using Gise.Infrastructure.Mail;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Tickets;

/// <summary>Issues and voids tickets alongside the order change that causes it, and queues the email that tells the buyer.</summary>
internal sealed class TicketBook(GiseDbContext db)
{
    /// <summary>Within the caller's transaction; the caller saves.</summary>
    public async Task IssueAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Tickets.AnyAsync(t => t.OrderId == order.Id, ct))
        {
            return;
        }

        var buyer = await db.Users.AsNoTracking().Where(u => u.Id == order.UserId).Select(u => new { u.DisplayName, u.Email }).SingleAsync(ct);
        db.Tickets.AddRange(Ticket.Issue(order, buyer.DisplayName, now));
        db.Emails.Add(OutboxEmail.For(EmailKind.Tickets, order.Id, buyer.Email, now));
    }

    /// <summary>Voids the order's tickets at once and queues the refund email. Within the caller's transaction; the caller saves.</summary>
    public async Task VoidAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        await db.Tickets.Where(t => t.OrderId == order.Id && t.Status == TicketStatus.Valid)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TicketStatus.Void).SetProperty(t => t.VoidedAt, now), ct);
        var email = await db.Users.AsNoTracking().Where(u => u.Id == order.UserId).Select(u => u.Email).SingleAsync(ct);
        db.Emails.Add(OutboxEmail.For(EmailKind.Refund, order.Id, email, now));
    }
}
