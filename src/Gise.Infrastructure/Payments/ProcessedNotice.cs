namespace Gise.Infrastructure.Payments;

/// <summary>A webhook that has been acted on. Providers deliver at least once; the second delivery finds this and stops.</summary>
public sealed class ProcessedNotice
{
    private ProcessedNotice()
    {
    }

    public string Provider { get; private set; } = string.Empty;

    public string NoticeId { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public Guid OrderId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public static ProcessedNotice From(string provider, PaymentNotice notice, DateTimeOffset now) =>
        new() { Provider = provider, NoticeId = notice.EventId, Type = notice.Type, OrderId = notice.OrderId, ReceivedAt = now };
}
