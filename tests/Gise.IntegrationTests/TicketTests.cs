using System.Net;
using System.Net.Http.Json;
using System.Text;
using Gise.Infrastructure.Payments;
using Gise.IntegrationTests.Support;
using UglyToad.PdfPig;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TicketTests(ApiFactory factory)
{
    [Fact]
    public async Task A_paid_order_gets_one_signed_ticket_per_place_and_an_email_with_the_pdf()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: "Kültür Gecesi: Şiir & Müzik"));
        using var buyer = await factory.RegisterAsync("Gülşen Çağlayan");
        var order = await factory.BuyAsync(buyer, @event.Id, new
        {
            seats = new[] { new { section = "A", row = "C", number = 4 } },
            standing = new[] { new { section = "floor", quantity = 1 } },
        });

        var tickets = await (await buyer.Http.GetAsync("/api/tickets")).ReadAsync<List<MyTicketDto>>();
        await factory.SendEmailsAsync();

        var mine = tickets.Where(t => t.OrderId == order.Id).ToList();
        Assert.Equal(["Parter · Row C · Seat 4", "Ayakta"], mine.Select(t => t.Label).Order(StringComparer.Ordinal).Reverse());
        Assert.All(mine, t => Assert.Matches("^GS1\\.[A-Za-z0-9_-]{50}\\.[A-Za-z0-9_-]{86}$", t.Code!));
        Assert.All(mine, t => Assert.Equal(("Valid", "Gülşen Çağlayan"), (t.Status, t.Holder)));
        var email = Assert.Single(factory.Mail.To(buyer.Email));
        Assert.Equal("Your tickets: Kültür Gecesi: Şiir & Müzik", email.Subject);
        Assert.Contains(order.Number, email.Text, StringComparison.Ordinal);
        Assert.Contains("Kültür Gecesi: Şiir &amp; Müzik", email.Html, StringComparison.Ordinal);
        var pdf = Assert.Single(email.Attachments);
        Assert.Equal(($"gise-{order.Number}.pdf", "application/pdf"), (pdf.FileName, pdf.ContentType));
        using var document = PdfDocument.Open(pdf.Content);
        Assert.Equal(2, document.NumberOfPages);
        var text = string.Join(' ', document.GetPages().Select(p => p.Text));
        Assert.Contains("Şiir", text, StringComparison.Ordinal);
        Assert.Contains("Gülşen", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Buyers_download_their_tickets_and_codes_but_nobody_else_can()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        using var stranger = await factory.RegisterAsync("Başkası");
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Standing(2));
        var ticket = (await (await buyer.Http.GetAsync("/api/tickets")).ReadAsync<List<MyTicketDto>>()).First(t => t.OrderId == order.Id);

        var qr = await buyer.Http.GetAsync($"/api/tickets/{ticket.Id}/qr.png");
        var pdf = await buyer.Http.GetAsync($"/api/orders/{order.Id}/tickets.pdf");

        Assert.Equal("image/png", qr.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], (await qr.Content.ReadAsByteArrayAsync())[..4]);
        Assert.Equal("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/tickets/{ticket.Id}/qr.png")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/orders/{order.Id}/tickets.pdf")).StatusCode);
        Assert.DoesNotContain(await (await stranger.Http.GetAsync("/api/tickets")).ReadAsync<List<MyTicketDto>>(), t => t.Id == ticket.Id);
    }

    [Fact]
    public async Task A_refund_voids_the_tickets_and_says_so_by_email()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Standing(1));
        var code = (await (await buyer.Http.GetAsync("/api/tickets")).ReadAsync<List<MyTicketDto>>()).Single(t => t.OrderId == order.Id).Code;

        await buyer.Http.PostAsync($"/api/orders/{order.Id}/refund", null);
        await factory.SendEmailsAsync();
        var after = (await (await buyer.Http.GetAsync("/api/tickets")).ReadAsync<List<MyTicketDto>>()).Single(t => t.OrderId == order.Id);
        var scan = await organizer.ScanAsync(@event.Id, code!, "Kuzey");

        Assert.Equal(("Void", null), (after.Status, after.Code));
        Assert.Equal("Void", scan.Outcome);
        Assert.Contains(factory.Mail.To(buyer.Email), m => m.Subject == $"Refund for order {order.Number}" && m.Text.Contains("You asked for a refund", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.Http.GetAsync($"/api/orders/{order.Id}/tickets.pdf")).StatusCode);
    }

    [Fact]
    public async Task An_email_the_mail_server_refuses_is_sent_again_later()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        await factory.SendEmailsAsync();
        await factory.BuyAsync(buyer, @event.Id, Buying.Standing(1));

        factory.Mail.FailNext = 1;
        await factory.SendEmailsAsync();
        Assert.Empty(factory.Mail.To(buyer.Email));
        Assert.Equal(1, await factory.ScalarAsync<int>("SELECT attempts FROM outbox_emails WHERE \"to\" = @p0", buyer.Email));

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await factory.SendEmailsAsync();

        Assert.Single(factory.Mail.To(buyer.Email));
    }

    [Fact]
    public async Task Cancelling_the_event_voids_every_ticket()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        await factory.BuyAsync(buyer, @event.Id, Buying.Standing(2));

        await organizer.Owner.Http.PostAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/cancel", null);

        Assert.Equal(2, await factory.ScalarAsync<long>("SELECT count(*) FROM tickets WHERE event_id = @p0 AND status = 'Void'", @event.Id));
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM tickets WHERE event_id = @p0 AND status = 'Valid'", @event.Id));
    }
}
