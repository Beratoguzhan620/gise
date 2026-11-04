using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Gise.Infrastructure.Mail;

public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record MailToSend(string To, string Subject, string Text, string Html, IReadOnlyList<MailAttachment> Attachments);

public interface IMailTransport
{
    Task SendAsync(MailToSend mail, CancellationToken ct);
}

/// <summary>Plain SMTP. Locally it talks to Mailpit, which keeps every message for viewing instead of delivering it.</summary>
internal sealed class SmtpMailTransport(IOptions<MailOptions> options) : IMailTransport
{
    public async Task SendAsync(MailToSend mail, CancellationToken ct)
    {
        var settings = options.Value;
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = mail.Subject,
            Body = mail.Text,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        message.To.Add(mail.To);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(mail.Html, System.Text.Encoding.UTF8, "text/html"));
        foreach (var attachment in mail.Attachments)
        {
            message.Attachments.Add(new Attachment(new MemoryStream(attachment.Content), attachment.FileName, attachment.ContentType));
        }

        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.UseSsl };
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        await client.SendMailAsync(message, ct);
    }
}
