namespace Gise.Infrastructure.Mail;

public sealed class MailOptions
{
    public const string Section = "Mail";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseSsl { get; set; }

    public string? UserName { get; set; }

    /// <summary>From user secrets or the environment.</summary>
    public string? Password { get; set; }

    public string FromAddress { get; set; } = "tickets@gise.local";

    public string FromName { get; set; } = "Gişe";

    /// <summary>How often waiting emails are sent; 0 turns sending off (tests send when they choose to).</summary>
    public int PollSeconds { get; set; } = 5;

    public int MaxAttempts { get; set; } = 8;
}
