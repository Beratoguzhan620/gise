using Gise.Core;
using Gise.Infrastructure.Payments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gise.Api.Features.Sales;

public sealed record WebhookResponse(bool Received, NoticeOutcome Outcome);

internal static partial class WebhookEndpoints
{
    private const int MaxBodyBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/webhooks/{provider}", Receive).WithTags("Payments").AllowAnonymous()
            .WithSummary("Where the payment provider reports outcomes")
            .WithDescription("Signed by the provider. Unsigned, wrongly signed or stale notices are refused; repeated ones are acknowledged and ignored.");
        return app;
    }

    private static async Task<Ok<WebhookResponse>> Receive(
        string provider, HttpRequest request, IEnumerable<IWebhookVerifier> verifiers, PaymentProcessor processor, TimeProvider clock, ILoggerFactory loggers,
        CancellationToken ct)
    {
        var verifier = verifiers.FirstOrDefault(v => string.Equals(v.Provider, provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException(ErrorCodes.NotFound, "Unknown payment provider.");

        if (request.ContentLength > MaxBodyBytes)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The notice is too large.");
        }

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var notice = verifier.Verify(headers, body, clock.GetUtcNow());
        if (notice is null)
        {
            LogRejected(loggers.CreateLogger(typeof(WebhookEndpoints)), provider);
            throw new DomainException(ErrorCodes.InvalidSignature, "The notice's signature does not check out.");
        }

        var outcome = await processor.ApplyAsync(verifier.Provider, notice, ct);
        return TypedResults.Ok(new WebhookResponse(true, outcome));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a payment notice from {Provider} with a bad or stale signature")]
    private static partial void LogRejected(ILogger logger, string provider);
}
