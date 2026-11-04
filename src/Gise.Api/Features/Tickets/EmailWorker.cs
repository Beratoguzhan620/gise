using Gise.Infrastructure.Mail;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Tickets;

/// <summary>Sends what the outbox holds. Rows are claimed with SKIP LOCKED, so several instances never send the same email.</summary>
internal sealed partial class EmailWorker(
    IServiceScopeFactory scopes, IMailTransport transport, TimeProvider clock, IOptions<MailOptions> options, ILogger<EmailWorker> logger) : BackgroundService
{
    private const int Batch = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.PollSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                while (await SendDueAsync(stoppingToken) == Batch)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    /// <summary>Sends up to one batch of due emails; returns how many it tried.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GiseDbContext>();
        var composer = scope.ServiceProvider.GetRequiredService<EmailComposer>();
        var now = clock.GetUtcNow();
        var maxAttempts = options.Value.MaxAttempts;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var due = await db.Emails.FromSql($"""
            SELECT * FROM outbox_emails
            WHERE sent_at IS NULL AND attempts < {maxAttempts} AND next_attempt_at <= {now}
            ORDER BY next_attempt_at
            LIMIT {Batch}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);

        foreach (var email in due)
        {
            try
            {
                await transport.SendAsync(await composer.ComposeAsync(email, ct), ct);
                email.Sent(clock.GetUtcNow());
                LogSent(logger, email.Id, email.Kind, email.OrderId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.Failed(ex.Message, clock.GetUtcNow());
                LogRetry(logger, email.Id, email.Attempts, ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return due.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {EmailId} ({Kind}) sent for order {OrderId}")]
    private static partial void LogSent(ILogger logger, Guid emailId, EmailKind kind, Guid orderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {EmailId} not sent (attempt {Attempt}): {Reason}")]
    private static partial void LogRetry(ILogger logger, Guid emailId, int attempt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email sending failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
