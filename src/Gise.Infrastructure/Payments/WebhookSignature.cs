using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Gise.Infrastructure.Payments;

/// <summary>
/// Signs and checks webhook bodies the way most providers do: <c>t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;</c>.
/// The timestamp is part of what is signed, so an old notice replayed later is refused.
/// </summary>
public static class WebhookSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string body, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"t={timestamp},v1={Hex(secret, timestamp, body)}";
    }

    public static bool IsValid(string secret, string? header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "t")
            {
                timestamp = pair[1];
            }
            else if (pair[0] == "v1")
            {
                signatures.Add(pair[1]);
            }
        }

        if (timestamp is null || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).TotalSeconds) > Tolerance.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Hex(secret, timestamp, body));
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(s)));
    }

    private static string Hex(string secret, string timestamp, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
}
