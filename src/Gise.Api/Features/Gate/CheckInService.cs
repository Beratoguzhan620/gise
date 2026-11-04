using Gise.Core;
using Gise.Core.Tickets;
using Gise.Infrastructure.Persistence;
using Gise.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Gate;

/// <summary>
/// Turns scans into check-ins. One scan or a device's offline backlog go through the same path: each scan is
/// recorded once by the id the device gave it, the ticket row is locked while it is checked, and scans are
/// taken in the order they were made, so within a batch the earliest one is the one that lets the holder in.
/// </summary>
internal sealed partial class CheckInService(GiseDbContext db, TicketCodes codes, TimeProvider clock, ILogger<CheckInService> logger)
{
    public const int MaxBatch = 500;

    public async Task<List<ScanResult>> ProcessAsync(Guid eventId, Guid staffId, IReadOnlyList<ScanRequest> scans, CancellationToken ct)
    {
        Check(scans);
        var now = clock.GetUtcNow();
        var ids = scans.Select(s => s.ScanId).ToList();
        var seen = await db.CheckIns.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var results = new List<ScanResult>();

        foreach (var scan in scans.DistinctBy(s => s.ScanId).OrderBy(s => s.ScannedAt ?? now))
        {
            if (seen.TryGetValue(scan.ScanId, out var earlier))
            {
                results.Add(await ResultAsync(earlier.Id, earlier.Outcome, earlier.TicketId, earlier.ScannedAt, ct));
                continue;
            }

            // A device clock running ahead must not put a scan in the future. Kept to the microsecond, as stored.
            var scannedAt = Microseconds(scan.ScannedAt is { } at && at < now ? at.ToUniversalTime() : now);
            var gate = scan.Gate.Trim();
            var device = string.IsNullOrWhiteSpace(scan.Device) ? "unknown" : scan.Device.Trim();
            var code = codes.Read(scan.Code);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            CheckInOutcome outcome;
            Guid? ticketId = null;
            if (code is null)
            {
                outcome = CheckInOutcome.Invalid;
            }
            else if (code.EventId != eventId)
            {
                outcome = CheckInOutcome.WrongEvent;
            }
            else
            {
                await db.Database.ExecuteSqlAsync($"SELECT 1 FROM tickets WHERE id = {code.TicketId} FOR UPDATE", ct);
                var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == code.TicketId && t.EventId == eventId, ct);
                if (ticket is null)
                {
                    outcome = CheckInOutcome.Invalid;
                }
                else
                {
                    await db.Entry(ticket).ReloadAsync(ct);
                    outcome = ticket.CheckIn(scannedAt, gate);
                    ticketId = ticket.Id;
                }
            }

            db.CheckIns.Add(CheckIn.Record(scan.ScanId, eventId, ticketId, gate, device, staffId, scannedAt, now, outcome));
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // The same scan synced from two places at once: the first one counts.
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                var first = await db.CheckIns.AsNoTracking().SingleAsync(c => c.Id == scan.ScanId, ct);
                results.Add(await ResultAsync(first.Id, first.Outcome, first.TicketId, first.ScannedAt, ct));
                continue;
            }

            db.ChangeTracker.Clear();
            if (outcome is not CheckInOutcome.Admitted)
            {
                LogRefused(logger, eventId, gate, outcome);
            }

            results.Add(await ResultAsync(scan.ScanId, outcome, ticketId, scannedAt, ct));
        }

        return results;
    }

    private async Task<ScanResult> ResultAsync(Guid scanId, CheckInOutcome outcome, Guid? ticketId, DateTimeOffset scannedAt, CancellationToken ct)
    {
        var ticket = ticketId is null ? null : await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ticketId, ct);
        return new ScanResult(scanId, outcome, ticket is null ? null : GateEndpoints.ToGate(ticket), scannedAt, ticket?.CheckedInAt, ticket?.CheckedInGate);
    }

    private static DateTimeOffset Microseconds(DateTimeOffset value) => new(value.Ticks - value.Ticks % 10, value.Offset);

    private static void Check(IReadOnlyList<ScanRequest> scans)
    {
        if (scans.Count is 0 or > MaxBatch)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Send 1 to {MaxBatch} scans at a time.");
        }

        foreach (var scan in scans)
        {
            if (scan.ScanId == Guid.Empty)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, "Every scan needs its own id.");
            }

            if (scan.Gate?.Trim().Length is not (>= 1 and <= Ticket.GateMaxLength))
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"A gate name has 1 to {Ticket.GateMaxLength} characters.");
            }

            if (scan.Device?.Trim().Length > CheckIn.DeviceMaxLength)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"A device name has at most {CheckIn.DeviceMaxLength} characters.");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Gate {Gate} of event {Event} turned a scan away: {Outcome}")]
    private static partial void LogRefused(ILogger logger, Guid @event, string gate, CheckInOutcome outcome);
}
