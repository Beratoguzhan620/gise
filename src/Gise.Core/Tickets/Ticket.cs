using Gise.Core.Sales;

namespace Gise.Core.Tickets;

public enum TicketStatus
{
    Valid = 0,

    /// <summary>Refunded or the event was cancelled: the gate turns it away.</summary>
    Void = 1,
}

/// <summary>One entry to one event: issued when an order is paid, one per place, checked once at the gate.</summary>
public sealed class Ticket
{
    public const int HolderMaxLength = 50;
    public const int GateMaxLength = 30;

    private Ticket()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid EventId { get; private set; }

    public Guid PlaceId { get; private set; }

    public Guid UserId { get; private set; }

    public string HolderName { get; private set; } = string.Empty;

    public string SectionName { get; private set; } = string.Empty;

    public string? Row { get; private set; }

    public int Number { get; private set; }

    public string CategoryName { get; private set; } = string.Empty;

    public long PriceMinor { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>The earliest scan that let this ticket in, wherever it was made.</summary>
    public DateTimeOffset? CheckedInAt { get; private set; }

    public string? CheckedInGate { get; private set; }

    /// <summary>"Parter · Row C · Seat 12", or just "Ayakta" in a standing area.</summary>
    public string Label => Row is null ? SectionName : $"{SectionName} · Row {Row} · Seat {Number}";

    public static IReadOnlyList<Ticket> Issue(Order order, string holderName, DateTimeOffset now) =>
        order.Lines.Select(line => new Ticket
        {
            Id = Guid.CreateVersion7(now),
            OrderId = order.Id,
            EventId = order.EventId,
            PlaceId = line.PlaceId,
            UserId = order.UserId,
            HolderName = holderName.Length > HolderMaxLength ? holderName[..HolderMaxLength] : holderName,
            SectionName = line.SectionName,
            Row = line.Row,
            Number = line.Number,
            CategoryName = line.CategoryName,
            PriceMinor = line.PriceMinor,
            Status = TicketStatus.Valid,
            IssuedAt = now,
        }).ToList();

    /// <summary>
    /// A scan at the gate. The first scan lets the holder in; any later one is a second use. When scans made offline
    /// arrive out of order, the ticket keeps the earliest time and gate, but whichever scan arrives second is still
    /// the one reported as the repeat.
    /// </summary>
    public CheckInOutcome CheckIn(DateTimeOffset scannedAt, string gate)
    {
        if (Status == TicketStatus.Void)
        {
            return CheckInOutcome.Void;
        }

        if (CheckedInAt is { } first)
        {
            if (scannedAt < first)
            {
                CheckedInAt = scannedAt;
                CheckedInGate = gate;
            }

            return CheckInOutcome.AlreadyUsed;
        }

        CheckedInAt = scannedAt;
        CheckedInGate = gate;
        return CheckInOutcome.Admitted;
    }

    public void Void(DateTimeOffset now)
    {
        if (Status == TicketStatus.Valid)
        {
            Status = TicketStatus.Void;
            VoidedAt = now;
        }
    }
}

public enum CheckInOutcome
{
    /// <summary>Let in.</summary>
    Admitted = 0,

    /// <summary>The ticket was scanned before, here or at another gate.</summary>
    AlreadyUsed = 1,

    /// <summary>The ticket was refunded or the event cancelled.</summary>
    Void = 2,

    /// <summary>A real ticket, for another event.</summary>
    WrongEvent = 3,

    /// <summary>Not a ticket we signed: forged, damaged or from somewhere else.</summary>
    Invalid = 4,
}

/// <summary>One scan as a gate device reported it. Its id comes from the device, so a scan synced twice counts once.</summary>
public sealed class CheckIn
{
    public const int DeviceMaxLength = 40;

    private CheckIn()
    {
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public Guid? TicketId { get; private set; }

    public string Gate { get; private set; } = string.Empty;

    public string Device { get; private set; } = string.Empty;

    public Guid StaffId { get; private set; }

    public DateTimeOffset ScannedAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public CheckInOutcome Outcome { get; private set; }

    public static CheckIn Record(Guid scanId, Guid eventId, Guid? ticketId, string gate, string device, Guid staffId, DateTimeOffset scannedAt, DateTimeOffset now, CheckInOutcome outcome) =>
        new()
        {
            Id = scanId,
            EventId = eventId,
            TicketId = ticketId,
            Gate = gate,
            Device = device,
            StaffId = staffId,
            ScannedAt = scannedAt,
            ReceivedAt = now,
            Outcome = outcome,
        };
}
