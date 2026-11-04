using System.Collections.Concurrent;
using Gise.Infrastructure.Mail;

namespace Gise.IntegrationTests.Support;

public sealed class FakeMail : IMailTransport
{
    public ConcurrentQueue<MailToSend> Sent { get; } = new();

    /// <summary>How many sends fail before they start working again.</summary>
    public int FailNext { get; set; }

    public Task SendAsync(MailToSend mail, CancellationToken ct)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new InvalidOperationException("SMTP server unavailable");
        }

        Sent.Enqueue(mail);
        return Task.CompletedTask;
    }

    public IReadOnlyList<MailToSend> To(string address) => Sent.Where(m => m.To == address).ToList();
}
