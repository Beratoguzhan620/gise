namespace Gise.Core.Sales;

public enum HoldStatus
{
    /// <summary>The places are the buyer's until <see cref="Hold.ExpiresAt"/>.</summary>
    Active = 0,

    /// <summary>The buyer let them go.</summary>
    Released = 1,

    /// <summary>Time ran out before checkout.</summary>
    Expired = 2,

    /// <summary>Turned into an order that is being paid or was paid.</summary>
    Ordered = 3,
}

/// <summary>Places set aside for one buyer for a few minutes while they decide and pay. A buyer has one per event.</summary>
public sealed class Hold
{
    private Hold()
    {
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public Guid UserId { get; private set; }

    public HoldStatus Status { get; private set; }

    public int Places { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static Hold Create(Guid eventId, Guid userId, int places, DateTimeOffset now, TimeSpan duration) =>
        new() { Id = Guid.CreateVersion7(now), EventId = eventId, UserId = userId, Status = HoldStatus.Active, Places = places, CreatedAt = now, ExpiresAt = now + duration };

    public bool IsActive(DateTimeOffset now) => Status == HoldStatus.Active && ExpiresAt > now;

    public void Release()
    {
        if (Status == HoldStatus.Active)
        {
            Status = HoldStatus.Released;
        }
    }

    public void Expire()
    {
        if (Status == HoldStatus.Active)
        {
            Status = HoldStatus.Expired;
        }
    }

    /// <summary>Checkout: the places stay set aside until the payment deadline.</summary>
    public void Order(DateTimeOffset now, DateTimeOffset paymentDeadline)
    {
        if (!IsActive(now))
        {
            throw new DomainException(ErrorCodes.HoldExpired, "The time to check out ran out and the places went back on sale. Choose again.");
        }

        Status = HoldStatus.Ordered;
        ExpiresAt = paymentDeadline;
    }
}
