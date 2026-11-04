using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gise.Infrastructure.Payments;

namespace Gise.IntegrationTests.Support;

public static class Buying
{
    public static object Seats(params string[] seats) => new
    {
        seats = seats.Select(s => s.Split('-')).Select(p => new { section = p[0], row = p[1], number = int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) }).ToArray(),
    };

    public static object Standing(int quantity, string section = "floor") => new { standing = new[] { new { section, quantity } } };

    public static Task<HttpResponseMessage> HoldAsync(this Person buyer, Guid eventId, object body) =>
        buyer.Http.PostAsJsonAsync($"/api/events/{eventId}/holds", body);

    public static async Task<HoldDto> HeldAsync(this Person buyer, Guid eventId, object body)
    {
        var response = await buyer.HoldAsync(eventId, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<HoldDto>();
    }

    public static async Task<OrderDto> CheckoutAsync(this Person buyer, Guid holdId)
    {
        var response = await buyer.Http.PostAsync($"/api/holds/{holdId}/checkout", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrderDto>();
    }

    public static async Task<OrderDto> OrderAsync(this Person buyer, Guid orderId) =>
        await (await buyer.Http.GetAsync($"/api/orders/{orderId}")).ReadAsync<OrderDto>();

    /// <summary>Holds, checks out and pays: the order as the buyer sees it afterwards.</summary>
    public static async Task<OrderDto> BuyAsync(this ApiFactory factory, Person buyer, Guid eventId, object body)
    {
        var hold = await buyer.HeldAsync(eventId, body);
        var order = await buyer.CheckoutAsync(hold.Id);
        (await factory.NotifyAsync(order, PaymentState.Succeeded)).EnsureSuccessStatusCode();
        return await buyer.OrderAsync(order.Id);
    }

    /// <summary>What the provider's webhook would say about an order's payment, signed like the real thing.</summary>
    public static Task<HttpResponseMessage> NotifyAsync(
        this ApiFactory factory, OrderDto order, PaymentState state, string? noticeId = null, string? secret = null, DateTimeOffset? signedAt = null)
    {
        var paymentId = order.CheckoutUrl?.Split('/').Last() ?? factory.Payments.States.Keys.Single(k => k.EndsWith(order.Id.ToString("N"), StringComparison.Ordinal));
        if (state is PaymentState.Succeeded or PaymentState.Failed)
        {
            factory.Payments.States[paymentId] = state;
        }

        var status = state switch { PaymentState.Succeeded => "succeeded", PaymentState.Failed => "failed", PaymentState.Refunded => "refunded", _ => "pending" };
        var body = JsonSerializer.Serialize(new
        {
            id = noticeId ?? $"evt_{Guid.NewGuid():N}",
            type = $"payment.{status}",
            created = factory.Clock.GetUtcNow().ToUnixTimeSeconds(),
            data = new { payment = new { id = paymentId, reference = order.Id.ToString(), status, amount = (long)(order.Amount * 100), currency = "TRY" } },
        });
        return factory.SendNoticeAsync(body, WebhookSignature.Sign(secret ?? ApiFactory.WebhookSecret, body, signedAt ?? factory.Clock.GetUtcNow()));
    }

    public static async Task<HttpResponseMessage> SendNoticeAsync(this ApiFactory factory, string body, string signature)
    {
        using var anonymous = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhooks/sandbox") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Sandbox-Signature", signature);
        return await anonymous.SendAsync(request);
    }

    public static Task<long> CountPlacesAsync(this ApiFactory factory, Guid eventId, string status) =>
        factory.ScalarAsync<long>("SELECT count(*) FROM places WHERE event_id = @p0 AND status = @p1", eventId, status);
}

public static class Gate
{
    public static async Task<ScanResultDto> ScanAsync(this Organizer organizer, Guid eventId, string code, string gate, DateTimeOffset? scannedAt = null, Person? staff = null) =>
        (await organizer.SyncAsync(eventId, [new { scanId = Guid.NewGuid(), code, gate, device = $"{gate}-1", scannedAt }], staff)).Single();

    public static async Task<List<ScanResultDto>> SyncAsync(this Organizer organizer, Guid eventId, object[] scans, Person? staff = null)
    {
        var response = await (staff ?? organizer.Owner).Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/events/{eventId}/check-ins", new { scans });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<ScanResultDto>>();
    }

    public static async Task<List<string>> CodesAsync(this Person buyer, Guid orderId) =>
        (await (await buyer.Http.GetAsync("/api/tickets")).ReadAsync<List<MyTicketDto>>()).Where(t => t.OrderId == orderId).Select(t => t.Code!).ToList();
}
