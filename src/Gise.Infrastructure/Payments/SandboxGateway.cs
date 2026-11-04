using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Gise.Infrastructure.Payments;

/// <summary>The bundled test provider (<c>src/Gise.PaymentSandbox</c>): a hosted payment page and signed webhooks, no real money.</summary>
internal sealed class SandboxGateway(HttpClient http, IOptions<PaymentOptions> options) : IPaymentGateway
{
    public const string ProviderName = "Sandbox";

    public string Name => ProviderName;

    public async Task<PaymentSession> CreatePaymentAsync(PaymentRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
        {
            Content = JsonContent.Create(new
            {
                reference = request.OrderId.ToString(),
                amount = request.AmountMinor,
                currency = request.Currency,
                description = $"{request.OrderNumber} · {request.Description}",
                returnUrl = request.ReturnUrl,
                expiresAt = request.ExpiresAt,
            }, options: SandboxJson.Options),
        };
        // A retried checkout must not open a second payment for the same order.
        message.Headers.Add("Idempotency-Key", $"order-{request.OrderId:N}");
        var payment = await SendAsync<SandboxPayment>(message, ct);
        return new PaymentSession(payment.Id, payment.CheckoutUrl ?? throw new PaymentProviderException("The provider returned no checkout page."));
    }

    public async Task<PaymentState> GetStateAsync(string paymentId, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(paymentId)}");
        return SandboxJson.State((await SendAsync<SandboxPayment>(message, ct)).Status);
    }

    public async Task RefundAsync(string paymentId, long amountMinor, string idempotencyKey, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(paymentId)}/refunds")
        {
            Content = JsonContent.Create(new { amount = amountMinor }, options: SandboxJson.Options),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        await SendAsync<JsonElement>(message, ct);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, CancellationToken ct)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PaymentProviderException("The payment provider could not be reached.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentProviderException($"The payment provider answered {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<T>(SandboxJson.Options, ct)
                ?? throw new PaymentProviderException("The payment provider sent an empty answer.");
        }
    }

    private sealed record SandboxPayment(string Id, string Status, string? CheckoutUrl);
}

internal sealed class SandboxWebhookVerifier(IOptions<PaymentOptions> options) : IWebhookVerifier
{
    public const string SignatureHeader = "Sandbox-Signature";

    public string Provider => SandboxGateway.ProviderName;

    public PaymentNotice? Verify(IReadOnlyDictionary<string, string> headers, string body, DateTimeOffset now)
    {
        if (!headers.TryGetValue(SignatureHeader, out var signature)
            || !WebhookSignature.IsValid(options.Value.WebhookSecret ?? string.Empty, signature, body, now))
        {
            return null;
        }

        try
        {
            var notice = JsonSerializer.Deserialize<SandboxEvent>(body, SandboxJson.Options);
            return notice?.Data?.Payment is { } payment && Guid.TryParse(payment.Reference, out var orderId) && !string.IsNullOrEmpty(notice.Id)
                ? new PaymentNotice(notice.Id, notice.Type, payment.Id, orderId, SandboxJson.State(payment.Status))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SandboxEvent(string Id, string Type, SandboxEventData? Data);

    private sealed record SandboxEventData(SandboxEventPayment? Payment);

    private sealed record SandboxEventPayment(string Id, string Reference, string Status);
}

internal static class SandboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PaymentState State(string status) => status switch
    {
        "succeeded" => PaymentState.Succeeded,
        "failed" => PaymentState.Failed,
        "refunded" => PaymentState.Refunded,
        _ => PaymentState.Pending,
    };
}
