using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Gise.PaymentSandbox;

internal sealed class Payment
{
    public required string Id { get; init; }

    public required string Reference { get; init; }

    public required long Amount { get; init; }

    public required string Currency { get; init; }

    public required string Description { get; init; }

    public required string ReturnUrl { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary><c>pending</c>, <c>succeeded</c>, <c>failed</c> or <c>refunded</c>; <c>expired</c> is worked out from the time.</summary>
    public string Status { get; set; } = "pending";

    public long Refunded { get; set; }

    public string StatusAt(DateTimeOffset now) => Status == "pending" && now > ExpiresAt ? "expired" : Status;
}

/// <summary>Payments live in memory: this is a stand-in for a real provider, and a restart forgets everything.</summary>
internal sealed class PaymentStore
{
    private readonly ConcurrentDictionary<string, Payment> _payments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _idempotency = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public Payment? Find(string id) => _payments.GetValueOrDefault(id);

    /// <summary>The same idempotency key always returns the payment it first created.</summary>
    public Payment Create(string? idempotencyKey, Func<string, Payment> create)
    {
        lock (_gate)
        {
            if (idempotencyKey is not null && _idempotency.TryGetValue(idempotencyKey, out var existing))
            {
                return _payments[existing];
            }

            var payment = create(NewId("pay"));
            _payments[payment.Id] = payment;
            if (idempotencyKey is not null)
            {
                _idempotency[idempotencyKey] = payment.Id;
            }

            return payment;
        }
    }

    /// <summary>Moves a payment from one status to another; <c>false</c> if it was not in <paramref name="from"/>.</summary>
    public bool Transition(Payment payment, string from, string to, Action? alsoDo = null)
    {
        lock (_gate)
        {
            if (payment.Status != from)
            {
                return false;
            }

            payment.Status = to;
            alsoDo?.Invoke();
            return true;
        }
    }

    /// <summary>Records a refund once per idempotency key; returns the refund id and whether it is new.</summary>
    public (string RefundId, bool Created) Refund(Payment payment, string? idempotencyKey)
    {
        lock (_gate)
        {
            var key = $"refund:{idempotencyKey ?? Guid.NewGuid().ToString()}";
            if (_idempotency.TryGetValue(key, out var existing))
            {
                return (existing, false);
            }

            var id = NewId("re");
            _idempotency[key] = id;
            payment.Status = "refunded";
            payment.Refunded = payment.Amount;
            return (id, true);
        }
    }

    public static string NewId(string prefix) => $"{prefix}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
}
