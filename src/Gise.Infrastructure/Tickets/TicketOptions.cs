namespace Gise.Infrastructure.Tickets;

public sealed class TicketOptions
{
    public const string Section = "Tickets";

    /// <summary>An ECDSA P-256 private key in PEM (PKCS#8), from a secret store or the environment, never from the repository.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Local convenience: without a configured key, create one on first start and keep it in the database.</summary>
    public bool GenerateKeyIfMissing { get; set; }

    /// <summary>Times on tickets and in emails are shown in this zone.</summary>
    public string TimeZone { get; set; } = "Europe/Istanbul";
}
