using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Gise.PaymentSandbox;

/// <summary>The provider's hosted payment page. It asks for no card: a test provider only needs a yes or a no.</summary>
internal static class CheckoutPage
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public static string Render(Payment payment, string publicUrl, DateTimeOffset now)
    {
        var status = payment.StatusAt(now);
        var amount = (payment.Amount / 100m).ToString("N2", Turkish);
        var minutesLeft = Math.Max(0, (int)Math.Ceiling((payment.ExpiresAt - now).TotalMinutes));
        var action = $"{publicUrl.TrimEnd('/')}/checkout/{Uri.EscapeDataString(payment.Id)}";
        var body = status switch
        {
            "pending" => $"""
                <p class="muted">Time left to pay: about {minutesLeft} min</p>
                <form method="post" action="{action}/pay"><button class="primary" type="submit">Pay {amount} {E(payment.Currency)}</button></form>
                <form method="post" action="{action}/decline"><button class="secondary" type="submit">Decline the payment</button></form>
                <details>
                  <summary>Test a lost webhook</summary>
                  <p class="muted">Approves the payment without telling the shop. The shop finds out when it checks unpaid orders after their deadline.</p>
                  <form method="post" action="{action}/pay?notify=false"><button class="secondary" type="submit">Pay without notifying</button></form>
                </details>
                """,
            "expired" => """<p class="state">This payment link has expired. Go back to the shop and check out again.</p>""",
            _ => $"""<p class="state">This payment is {E(status)}.</p><a class="secondary link" href="{E(payment.ReturnUrl)}">Back to the shop</a>""",
        };

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Payment · Sandbox</title>
              <link rel="preconnect" href="https://fonts.googleapis.com">
              <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@500&family=IBM+Plex+Sans:wght@400;500;600&display=swap" rel="stylesheet">
              <style>
                :root { --bg: #F7F6F3; --surface: #FFFFFF; --line: #E2DFD8; --text: #1A1A1A; --muted: #6B6862; --primary: #1F2A37; }
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px 16px; background: var(--bg); color: var(--text);
                       font: 15px/1.5 'IBM Plex Sans', system-ui, sans-serif; }
                main { width: 100%; max-width: 420px; background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 28px; }
                .banner { display: inline-block; font: 500 12px 'IBM Plex Mono', monospace; letter-spacing: .04em; text-transform: uppercase;
                          color: var(--muted); border: 1px solid var(--line); border-radius: 6px; padding: 2px 8px; margin-bottom: 18px; }
                h1 { font-size: 15px; font-weight: 500; color: var(--muted); margin: 0 0 4px; }
                .amount { font-size: 32px; font-weight: 600; margin: 0 0 4px; font-variant-numeric: tabular-nums; }
                .description { margin: 0 0 20px; }
                .muted { color: var(--muted); font-size: 13px; margin: 0 0 16px; }
                .state { margin: 8px 0 16px; }
                form { margin: 0 0 10px; }
                button, .link { width: 100%; display: block; text-align: center; font: 500 15px 'IBM Plex Sans', sans-serif; border-radius: 6px; padding: 11px 14px; cursor: pointer; text-decoration: none; }
                .primary { background: var(--primary); color: #FFF; border: 1px solid var(--primary); }
                .secondary { background: var(--surface); color: var(--text); border: 1px solid var(--line); }
                button:focus-visible, .link:focus-visible, summary:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
                details { margin-top: 18px; border-top: 1px solid var(--line); padding-top: 14px; }
                summary { cursor: pointer; color: var(--muted); font-size: 13px; margin-bottom: 10px; }
                footer { margin-top: 18px; color: var(--muted); font-size: 12px; }
              </style>
            </head>
            <body>
              <main>
                <span class="banner">Test payment · no money moves</span>
                <h1>Gişe asks you to pay</h1>
                <p class="amount">{{amount}} {{E(payment.Currency)}}</p>
                <p class="description">{{E(payment.Description)}}</p>
                {{body}}
                <footer>Payment {{E(payment.Id)}}</footer>
              </main>
            </body>
            </html>
            """;
    }

    private static string E(string value) => Html.Encode(value);
}
