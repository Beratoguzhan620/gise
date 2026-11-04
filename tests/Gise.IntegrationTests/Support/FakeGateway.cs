using System.Collections.Concurrent;
using Gise.Infrastructure.Payments;

namespace Gise.IntegrationTests.Support;

/// <summary>Stands in for the provider's API; webhooks are sent by the tests themselves, signed like the real ones.</summary>
public sealed class FakeGateway : IPaymentGateway
{
    private int _next;

    public ConcurrentDictionary<string, PaymentState> States { get; } = new(StringComparer.Ordinal);

    /// <summary>Idempotency keys of refunds the provider accepted.</summary>
    public ConcurrentDictionary<string, long> Refunds { get; } = new(StringComparer.Ordinal);

    public ConcurrentBag<PaymentRequest> Requests { get; } = [];

    public bool Down { get; set; }

    public string Name => "Sandbox";

    public Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct)
    {
        if (Down)
        {
            throw new PaymentProviderException("down");
        }

        Requests.Add(request);
        var id = $"pay_test_{Interlocked.Increment(ref _next)}_{request.OrderId:N}";
        States[id] = PaymentState.Pending;
        return Task.FromResult(new PaymentSession(id, $"https://sandbox.test/checkout/{id}"));
    }

    public Task<PaymentState> GetStateAsync(string paymentId, CancellationToken ct) =>
        Down ? throw new PaymentProviderException("down") : Task.FromResult(States.GetValueOrDefault(paymentId));

    public Task RefundAsync(string paymentId, long amountMinor, string idempotencyKey, CancellationToken ct)
    {
        if (Down)
        {
            throw new PaymentProviderException("down");
        }

        Refunds.TryAdd(idempotencyKey, amountMinor);
        States[paymentId] = PaymentState.Refunded;
        return Task.CompletedTask;
    }
}
