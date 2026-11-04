namespace Gise.Infrastructure.Tickets;

/// <summary>A signing key created for a local run. Real deployments configure <see cref="TicketOptions.SigningKey"/> instead.</summary>
public sealed class TicketKey
{
    private TicketKey()
    {
    }

    public string Id { get; private set; } = string.Empty;

    public byte[] PrivateKey { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static TicketKey Create(string id, byte[] privateKey, DateTimeOffset now) => new() { Id = id, PrivateKey = privateKey, CreatedAt = now };
}
