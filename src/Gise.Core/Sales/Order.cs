using System.Security.Cryptography;
using Gise.Core.Events;

namespace Gise.Core.Sales;

public enum OrderStatus
{
    /// <summary>Waiting for the payment provider; the places stay set aside until the deadline.</summary>
    PendingPayment = 0,

    Paid = 1,

    /// <summary>The provider declined the payment.</summary>
    Failed = 2,

    /// <summary>Not paid in time; the places went back on sale.</summary>
    Expired = 3,

    /// <summary>Paid, and a refund has been asked for but not yet confirmed by the provider.</summary>
    RefundPending = 4,

    Refunded = 5,
}

public enum RefundReason
{
    Buyer = 0,
    Organizer = 1,
    EventCancelled = 2,

    /// <summary>The money arrived after the places had gone to someone else.</summary>
    LatePayment = 3,
}

/// <summary>A place as it was sold: names and price are copied, so the order reads the same whatever changes later.</summary>
public sealed class OrderLine
{
    public Guid PlaceId { get; set; }

    public string SectionKey { get; set; } = string.Empty;

    public string SectionName { get; set; } = string.Empty;

    public string? Row { get; set; }

    public int Number { get; set; }

    public Guid CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public long PriceMinor { get; set; }
}

public sealed class Order
{
    public const int NumberLength = 11;
    private const int RandomLength = 8;

    // No 0/O, 1/I/L: order numbers are read out over the phone.
    private const string NumberAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    private Order()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>What buyers and support staff quote, for example <c>GS-7KQ2M9XW</c>.</summary>
    public string Number { get; private set; } = string.Empty;

    public Guid EventId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid HoldId { get; private set; }

    public OrderStatus Status { get; private set; }

    public long AmountMinor { get; private set; }

    public string Currency { get; private set; } = Money.Currency;

    public List<OrderLine> Lines { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset PaymentDeadline { get; private set; }

    public string? PaymentProvider { get; private set; }

    public string? PaymentId { get; private set; }

    public string? CheckoutUrl { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public RefundReason? RefundReason { get; private set; }

    public DateTimeOffset? RefundRequestedAt { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    public static Order Create(Hold hold, IReadOnlyList<OrderLine> lines, DateTimeOffset now, DateTimeOffset paymentDeadline)
    {
        if (lines.Count == 0 || lines.Count != hold.Places)
        {
            throw new DomainException(ErrorCodes.HoldExpired, "Some of the places are no longer set aside for you. Choose again.");
        }

        return new Order
        {
            Id = Guid.CreateVersion7(now),
            Number = NewNumber(),
            EventId = hold.EventId,
            UserId = hold.UserId,
            HoldId = hold.Id,
            Status = OrderStatus.PendingPayment,
            AmountMinor = lines.Sum(l => l.PriceMinor),
            Lines = [.. lines],
            CreatedAt = now,
            PaymentDeadline = paymentDeadline,
        };
    }

    public void AttachPayment(string provider, string paymentId, string checkoutUrl)
    {
        PaymentProvider = provider;
        PaymentId = paymentId;
        CheckoutUrl = checkoutUrl;
    }

    /// <summary>Settles a successful payment. <c>false</c> when the order was no longer waiting for one.</summary>
    public bool MarkPaid(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.PendingPayment or OrderStatus.Expired or OrderStatus.Failed))
        {
            return false;
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        ClosedAt = null;
        return true;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        if (Status == OrderStatus.PendingPayment)
        {
            Status = OrderStatus.Failed;
            ClosedAt = now;
        }
    }

    public void MarkExpired(DateTimeOffset now)
    {
        if (Status == OrderStatus.PendingPayment)
        {
            Status = OrderStatus.Expired;
            ClosedAt = now;
        }
    }

    /// <summary>Paid money that cannot buy the places (they were sold to someone else meanwhile) goes back.</summary>
    public void RefundLatePayment(DateTimeOffset now)
    {
        Status = OrderStatus.RefundPending;
        PaidAt ??= now;
        RefundReason = Sales.RefundReason.LatePayment;
        RefundRequestedAt = now;
    }

    public void RequestRefund(RefundReason reason, DateTimeOffset now)
    {
        if (Status != OrderStatus.Paid)
        {
            throw new DomainException(ErrorCodes.InvalidState, Status is OrderStatus.RefundPending or OrderStatus.Refunded
                ? "This order is already being refunded."
                : "Only a paid order can be refunded.");
        }

        Status = OrderStatus.RefundPending;
        RefundReason = reason;
        RefundRequestedAt = now;
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status == OrderStatus.RefundPending)
        {
            Status = OrderStatus.Refunded;
            RefundedAt = now;
        }
    }

    /// <summary>Checks a buyer may refund this order of <paramref name="event"/> on their own.</summary>
    public void EnsureBuyerMayRefund(Event @event, DateTimeOffset now)
    {
        if (@event.RefundDeadline is not { } deadline)
        {
            throw new DomainException(ErrorCodes.RefundClosed, "The organizer handles refunds for this event. Contact them with your order number.");
        }

        if (now > deadline)
        {
            throw new DomainException(ErrorCodes.RefundClosed, $"Refunds for this event closed {@event.RefundCutoffHours} hours before the start.");
        }
    }

    private static string NewNumber() =>
        "GS-" + RandomNumberGenerator.GetString(NumberAlphabet, RandomLength);
}
