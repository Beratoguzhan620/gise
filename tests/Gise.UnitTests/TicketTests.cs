using System.Security.Cryptography;
using Gise.Core.Sales;
using Gise.Core.Tickets;
using Gise.Infrastructure.Tickets;
using UglyToad.PdfPig;

namespace Gise.UnitTests;

public sealed class TicketTests
{
    private static readonly DateTimeOffset Doors = Builders.Now;

    [Fact]
    public void A_code_reads_back_as_the_ticket_and_event_it_was_signed_for()
    {
        var codes = Codes();
        var (ticket, @event) = (Guid.NewGuid(), Guid.NewGuid());

        var code = codes.Sign(ticket, @event);

        Assert.StartsWith(TicketCodes.Prefix, code, StringComparison.Ordinal);
        Assert.InRange(code.Length, 130, 150);
        Assert.Equal(new TicketCode(ticket, @event), codes.Read(code));
        Assert.Equal(8, codes.KeyId.Length);
    }

    [Fact]
    public void Codes_from_another_key_or_with_any_change_do_not_read()
    {
        var ours = Codes();
        var theirs = Codes();
        var code = ours.Sign(Guid.NewGuid(), Guid.NewGuid());
        var payloadChanged = code.Remove(10, 1).Insert(10, code[10] == 'A' ? "B" : "A");

        Assert.Null(theirs.Read(code));
        Assert.Null(ours.Read(payloadChanged));
        Assert.Null(ours.Read(code.Replace("GS1.", "GS2.", StringComparison.Ordinal)));
        Assert.Null(ours.Read(code + ".extra"));
        Assert.Null(ours.Read("GS1.%%%.***"));
        Assert.Null(ours.Read(null));
    }

    [Fact]
    public void Only_p256_keys_sign_tickets()
    {
        using var rsaLike = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<InvalidOperationException>(() => new TicketCodes().Load(rsaLike));
    }

    [Fact]
    public void The_first_scan_lets_the_holder_in_and_any_later_one_is_a_repeat()
    {
        var ticket = Issue().Single();

        Assert.Equal(CheckInOutcome.Admitted, ticket.CheckIn(Doors, "Kuzey"));
        Assert.Equal(CheckInOutcome.AlreadyUsed, ticket.CheckIn(Doors.AddMinutes(1), "Güney"));
        Assert.Equal((Doors, "Kuzey"), (ticket.CheckedInAt!.Value, ticket.CheckedInGate));
    }

    [Fact]
    public void An_earlier_scan_that_arrives_late_moves_the_check_in_time_back_but_still_counts_as_the_repeat()
    {
        var ticket = Issue().Single();
        ticket.CheckIn(Doors, "Kuzey");

        var late = ticket.CheckIn(Doors.AddSeconds(-30), "Güney");

        Assert.Equal(CheckInOutcome.AlreadyUsed, late);
        Assert.Equal((Doors.AddSeconds(-30), "Güney"), (ticket.CheckedInAt!.Value, ticket.CheckedInGate));
    }

    [Fact]
    public void A_void_ticket_never_gets_in()
    {
        var ticket = Issue().Single();
        ticket.Void(Doors);

        Assert.Equal(CheckInOutcome.Void, ticket.CheckIn(Doors, "Kuzey"));
        Assert.Null(ticket.CheckedInAt);
    }

    [Fact]
    public void Tickets_copy_the_order_lines_and_label_seats_and_standing_spots()
    {
        var tickets = Issue(withStanding: true);

        Assert.Equal(["Parter · Row C · Seat 12", "Ayakta"], tickets.Select(t => t.Label));
        Assert.All(tickets, t => Assert.Equal(TicketStatus.Valid, t.Status));
        Assert.Equal(2, tickets.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void The_pdf_has_a_page_per_ticket_in_turkish()
    {
        var codes = Codes();
        var pdf = TicketPdf.Render(new TicketDocument(
            "Karadeniz Ezgileri: Kemençe ve Şiir Gecesi, uzun bir başlıkla iki satıra taşan", Doors, "Atatürk Kültür Merkezi", "Samsun", "Cumhuriyet Mah.", "GS-7KQ2M9XW",
            [
                new PrintedTicket(Guid.NewGuid(), codes.Sign(Guid.NewGuid(), Guid.NewGuid()), "Parter · Row C · Seat 12", "Ön", 750m, "Çağrı Öztürk"),
                new PrintedTicket(Guid.NewGuid(), codes.Sign(Guid.NewGuid(), Guid.NewGuid()), "Ayakta", "Ayakta", 300m, "Çağrı Öztürk"),
            ]));

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(2, document.NumberOfPages);
        var first = document.GetPage(1);
        Assert.Contains("Kemençe", first.Text, StringComparison.Ordinal);
        Assert.Contains("Çağrı Öztürk", first.Text, StringComparison.Ordinal);
        Assert.Contains("GS-7KQ2M9XW", first.Text, StringComparison.Ordinal);
        Assert.InRange(pdf.Length, 1_000, 1_000_000);
    }

    private static TicketCodes Codes()
    {
        var codes = new TicketCodes();
        codes.Load(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        return codes;
    }

    private static IReadOnlyList<Ticket> Issue(bool withStanding = false)
    {
        var hold = Hold.Create(Guid.NewGuid(), Guid.NewGuid(), withStanding ? 2 : 1, Doors, TimeSpan.FromMinutes(8));
        List<OrderLine> lines = [new() { PlaceId = Guid.NewGuid(), SectionKey = "A", SectionName = "Parter", Row = "C", Number = 12, CategoryName = "Ön", PriceMinor = 75_000 }];
        if (withStanding)
        {
            lines.Add(new() { PlaceId = Guid.NewGuid(), SectionKey = "floor", SectionName = "Ayakta", Number = 7, CategoryName = "Ayakta", PriceMinor = 30_000 });
        }

        var order = Order.Create(hold, lines, Doors, Doors.AddMinutes(10));
        order.MarkPaid(Doors);
        return Ticket.Issue(order, "Deniz Aydın", Doors);
    }
}
