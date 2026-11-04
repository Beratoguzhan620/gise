using Gise.Core.Tickets;

namespace Gise.Api.Features.Gate;

public sealed record GateTicket(Guid Id, string Label, string Category, string Holder, TicketStatus Status, DateTimeOffset? CheckedInAt, string? Gate);

/// <summary>
/// Everything a gate device needs to work without a connection: the public key that checks signatures, and the
/// list of tickets with names, seats and which are void. Tickets bought after the download still pass on their signature.
/// </summary>
public sealed record GatePackage(
    Guid EventId, string Title, DateTimeOffset StartsAt, string VenueName, string KeyId, string PublicKey, IReadOnlyList<GateTicket> Tickets,
    DateTimeOffset GeneratedAt);

public sealed record ScanRequest(Guid ScanId, string Code, string Gate, string? Device = null, DateTimeOffset? ScannedAt = null);

public sealed record ScanBatch(List<ScanRequest> Scans);

public sealed record ScanResult(Guid ScanId, CheckInOutcome Outcome, GateTicket? Ticket, DateTimeOffset ScannedAt, DateTimeOffset? FirstCheckedInAt, string? FirstGate);

public sealed record GateCount(string Gate, int Admitted);

public sealed record RepeatScan(Guid TicketId, string Label, string Holder, DateTimeOffset FirstAt, string? FirstGate, DateTimeOffset AgainAt, string AgainGate, string AgainDevice);

public sealed record RecentScan(Guid ScanId, CheckInOutcome Outcome, string? Label, string Gate, string Device, DateTimeOffset ScannedAt);

public sealed record CheckInSummary(
    Guid EventId, int Tickets, int Admitted, int Remaining, IReadOnlyList<GateCount> Gates, IReadOnlyList<RepeatScan> Repeats,
    IReadOnlyList<RecentScan> Recent, int Rejected);
