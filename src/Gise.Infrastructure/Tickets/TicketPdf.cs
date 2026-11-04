using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Gise.Infrastructure.Tickets;

public sealed record PrintedTicket(Guid Id, string Code, string Label, string Category, decimal Price, string Holder);

public sealed record TicketDocument(
    string EventTitle, DateTimeOffset StartsAtLocal, string VenueName, string City, string Address, string OrderNumber, IReadOnlyList<PrintedTicket> Tickets);

/// <summary>One A4 page per ticket: what, when, where, which seat, and the code the gate scans. Set in IBM Plex, which has every Turkish letter.</summary>
public static class TicketPdf
{
    private const double Left = 48;
    private const double Width = 595 - 2 * Left;
    private static readonly byte[] Regular = Font("IBMPlexSans-Regular.ttf");
    private static readonly byte[] SemiBold = Font("IBMPlexSans-SemiBold.ttf");

    public static byte[] Render(TicketDocument document)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddTrueTypeFont(Regular);
        var bold = builder.AddTrueTypeFont(SemiBold);
        var total = document.Tickets.Count;

        for (var i = 0; i < total; i++)
        {
            var ticket = document.Tickets[i];
            var page = builder.AddPage(PageSize.A4);
            const double top = 800;

            // The stub: a burgundy band with the brand and the ticket's place in the order.
            page.SetTextAndFillColor(0x8E, 0x2F, 0x3C);
            page.SetStrokeColor(0x8E, 0x2F, 0x3C);
            page.DrawRectangle(new PdfPoint(Left, top - 44), Width, 44, 0, fill: true);
            page.SetTextAndFillColor(0xFF, 0xFF, 0xFF);
            page.AddText("Gişe", 18, new PdfPoint(Left + 18, top - 29), bold);
            page.AddText($"Ticket {i + 1} of {total}", 10, new PdfPoint(Left + Width - 18 - TextWidth(page, $"Ticket {i + 1} of {total}", 10, regular), top - 27), regular);

            var y = top - 84;
            page.SetTextAndFillColor(0x1A, 0x1A, 0x1A);
            foreach (var line in Wrap(page, document.EventTitle, 22, bold, Width))
            {
                page.AddText(line, 22, new PdfPoint(Left, y), bold);
                y -= 28;
            }

            page.SetTextAndFillColor(0x6B, 0x68, 0x62);
            page.AddText(document.StartsAtLocal.ToString("dddd, d MMMM yyyy · HH:mm", CultureInfo.InvariantCulture), 12, new PdfPoint(Left, y), regular);
            y -= 18;
            var place = string.IsNullOrEmpty(document.Address) ? $"{document.VenueName}, {document.City}" : $"{document.VenueName}, {document.Address}, {document.City}";
            foreach (var line in Wrap(page, place, 12, regular, Width))
            {
                page.AddText(line, 12, new PdfPoint(Left, y), regular);
                y -= 18;
            }

            // The body: details on the left, the code on the right.
            y -= 14;
            page.SetStrokeColor(0xE2, 0xDF, 0xD8);
            page.DrawRectangle(new PdfPoint(Left, y - 230), Width, 230, 1);
            const double qrSize = 190;
            DrawCode(page, ticket.Code, Left + Width - qrSize - 20, y - 20 - qrSize, qrSize);

            var rowY = y - 38;
            foreach (var (label, value) in new[]
            {
                ("PLACE", ticket.Label),
                ("CATEGORY", $"{ticket.Category} · {ticket.Price.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TRY"),
                ("HOLDER", ticket.Holder),
                ("ORDER", document.OrderNumber),
            })
            {
                page.SetTextAndFillColor(0x6B, 0x68, 0x62);
                page.AddText(label, 8, new PdfPoint(Left + 20, rowY + 14), regular);
                page.SetTextAndFillColor(0x1A, 0x1A, 0x1A);
                page.AddText(Fit(page, value, 13, bold, Width - qrSize - 70), 13, new PdfPoint(Left + 20, rowY), bold);
                rowY -= 48;
            }

            page.SetTextAndFillColor(0x6B, 0x68, 0x62);
            page.AddText("Show this code at the gate. It lets one person in, once.", 10, new PdfPoint(Left, y - 254), regular);
            page.AddText($"Ticket {ticket.Id}", 8, new PdfPoint(Left, y - 270), regular);
        }

        return builder.Build();
    }

    // Drawn as filled squares rather than an image: sharp at any zoom or print size, and a few kilobytes.
    private static void DrawCode(PdfPageBuilder page, string code, double left, double bottom, double size)
    {
        var modules = TicketQr.Modules(code);
        var count = modules.GetLength(0);
        var module = size / count;
        page.SetTextAndFillColor(0x00, 0x00, 0x00);
        page.SetStrokeColor(0x00, 0x00, 0x00);
        for (var row = 0; row < count; row++)
        {
            var y = bottom + size - (row + 1) * module;
            var start = -1;
            for (var column = 0; column <= count; column++)
            {
                var dark = column < count && modules[row, column];
                if (dark && start < 0)
                {
                    start = column;
                }
                else if (!dark && start >= 0)
                {
                    page.DrawRectangle(new PdfPoint(left + start * module, y), (column - start) * module, module, 0, fill: true);
                    start = -1;
                }
            }
        }
    }

    private static double TextWidth(PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font)
    {
        var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters[^1].EndBaseLine.X - letters[0].StartBaseLine.X;
    }

    private static IEnumerable<string> Wrap(PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font, double width)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && TextWidth(page, candidate, size, font) > width)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }

    private static string Fit(PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font, double width)
    {
        if (TextWidth(page, text, size, font) <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && TextWidth(page, cut + "…", size, font) > width)
        {
            cut = cut[..^1];
        }

        return cut.TrimEnd() + "…";
    }

    private static byte[] Font(string name)
    {
        using var stream = typeof(TicketPdf).Assembly.GetManifestResourceStream($"Gise.Infrastructure.Tickets.Fonts.{name}")
            ?? throw new InvalidOperationException($"The font {name} is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
