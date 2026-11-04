using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Gise.PaymentSandbox;

/// <summary>
/// Delivers outcomes to the shop like a real provider: signed, at least once, retried with growing pauses
/// when the shop does not answer 2xx.
/// </summary>
internal sealed partial class WebhookSender(IHttpClientFactory http, IOptions<SandboxOptions> options, TimeProvider clock, ILogger<WebhookSender> logger)
    : BackgroundService
{
    private static readonly TimeSpan[] Pauses = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Channel<(string Body, int Attempt)> _queue = Channel.CreateUnbounded<(string, int)>();

    public void Send(string type, Payment payment, DateTimeOffset now)
    {
        var body = JsonSerializer.Serialize(new
        {
            id = PaymentStore.NewId("evt"),
            type,
            created = now.ToUnixTimeSeconds(),
            data = new { payment = new { id = payment.Id, reference = payment.Reference, status = payment.StatusAt(now), amount = payment.Amount, currency = payment.Currency } },
        }, Json);
        _queue.Writer.TryWrite((body, 0));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (body, attempt) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (await DeliverAsync(body, stoppingToken))
            {
                continue;
            }

            if (attempt < Pauses.Length)
            {
                _ = RetryLaterAsync(body, attempt, stoppingToken);
            }
            else
            {
                LogGaveUp(logger, attempt + 1);
            }
        }
    }

    private async Task RetryLaterAsync(string body, int attempt, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Pauses[attempt], clock, ct);
            _queue.Writer.TryWrite((body, attempt + 1));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> DeliverAsync(string body, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return true;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Sandbox-Signature", Sign(settings.WebhookSecret ?? string.Empty, body, clock.GetUtcNow()));
        try
        {
            using var response = await http.CreateClient("webhooks").SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            LogRefused(logger, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, ex.Message);
        }

        return false;
    }

    /// <summary><c>t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;</c>.</summary>
    internal static string Sign(string secret, string body, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shop answered {Status} to a webhook; retrying")]
    private static partial void LogRefused(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shop could not be reached ({Reason}); retrying")]
    private static partial void LogUnreachable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up on a webhook after {Attempts} attempts")]
    private static partial void LogGaveUp(ILogger logger, int attempts);
}
