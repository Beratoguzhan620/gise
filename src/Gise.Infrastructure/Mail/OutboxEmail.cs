namespace Gise.Infrastructure.Mail;

public enum EmailKind
{
    /// <summary>The tickets of a paid order, with the PDF attached.</summary>
    Tickets = 0,

    /// <summary>An order is being refunded.</summary>
    Refund = 1,
}

/// <summary>
/// An email to send, written in the same transaction as the change it reports, so a paid order never goes
/// without its tickets email and a rolled-back one never gets one. A worker sends it and retries on failure.
/// </summary>
public sealed class OutboxEmail
{
    private OutboxEmail()
    {
    }

    public Guid Id { get; private set; }

    public EmailKind Kind { get; private set; }

    public Guid OrderId { get; private set; }

    public string To { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxEmail For(EmailKind kind, Guid orderId, string to, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), Kind = kind, OrderId = orderId, To = to, CreatedAt = now, NextAttemptAt = now };

    public void Sent(DateTimeOffset now)
    {
        SentAt = now;
        LastError = null;
    }

    /// <summary>Tries again later, a little later each time: 30 s, 1 min, 2 min … up to an hour.</summary>
    public void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 300 ? error[..300] : error;
        NextAttemptAt = now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Attempts - 1)));
    }
}
