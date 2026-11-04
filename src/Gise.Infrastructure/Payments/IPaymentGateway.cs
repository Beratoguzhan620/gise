namespace Gise.Infrastructure.Payments;

/// <summary>What the shop asks a payment provider to collect.</summary>
public sealed record PaymentRequest(
    Guid OrderId, string OrderNumber, long AmountMinor, string Currency, string Description, string ReturnUrl, DateTimeOffset ExpiresAt);

/// <summary>A payment the provider is ready to take: the buyer is sent to <see cref="CheckoutUrl"/>.</summary>
public sealed record PaymentSession(string PaymentId, string CheckoutUrl);

public enum PaymentState
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
}

/// <summary>Something the provider tells us happened, from a webhook or from asking.</summary>
public sealed record PaymentNotice(string EventId, string Type, string PaymentId, Guid OrderId, PaymentState State);

/// <summary>
/// A payment provider. The shop never sees card details: the buyer pays on the provider's page and the
/// provider reports the outcome in a signed webhook. Adding a provider means implementing this and
/// <see cref="IWebhookVerifier"/>.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct);

    /// <summary>For when a webhook went missing: asks the provider how a payment ended.</summary>
    Task<PaymentState> GetStateAsync(string paymentId, CancellationToken ct);

    /// <summary>Refunds in full. The idempotency key makes a retried refund a no-op at the provider.</summary>
    Task RefundAsync(string paymentId, long amountMinor, string idempotencyKey, CancellationToken ct);
}

public interface IWebhookVerifier
{
    string Provider { get; }

    /// <summary>Checks the signature and freshness, then reads the notice. <c>null</c> for anything not to be trusted.</summary>
    PaymentNotice? Verify(IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset now);
}

/// <summary>The provider could not be reached or refused the request; nothing was charged or refunded.</summary>
public sealed class PaymentProviderException(string message, Exception? inner = null) : Exception(message, inner);
