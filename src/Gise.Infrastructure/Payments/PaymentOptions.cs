namespace Gise.Infrastructure.Payments;

public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary><c>Sandbox</c> talks to the bundled test provider over HTTP. Other providers plug in behind the same interfaces.</summary>
    public string Provider { get; set; } = "Sandbox";

    /// <summary>The provider's API as this server reaches it, for example <c>http://payments:8080/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>From user secrets or the environment, never from the repository.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Shared with the provider to sign webhooks. From user secrets or the environment.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Where the provider sends the buyer afterwards; <c>{orderId}</c> is filled in.</summary>
    public string ReturnUrl { get; set; } = "http://localhost:8080/orders/{orderId}";

    /// <summary>How long a buyer has to pay once they check out.</summary>
    public int PaymentMinutes { get; set; } = 10;

    public int TimeoutSeconds { get; set; } = 15;
}
