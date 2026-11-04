namespace Gise.PaymentSandbox;

internal sealed class SandboxOptions
{
    public const string Section = "Sandbox";

    /// <summary>What the shop sends as a bearer token. From the environment, never from the repository.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Signs webhooks; the shop has the same value.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Where outcomes are posted, for example <c>http://api:8080/api/payments/webhooks/sandbox</c>.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>This service as the buyer's browser reaches it; checkout links start with it.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5601";
}
