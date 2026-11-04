using System.Security.Cryptography;
using System.Text;
using Gise.PaymentSandbox;
using Microsoft.Extensions.Options;

// A stand-in payment provider for local runs and tests: a hosted payment page, a small API, signed webhooks.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<SandboxOptions>()
    .Bind(builder.Configuration.GetSection(SandboxOptions.Section))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Sandbox:ApiKey is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.WebhookSecret), "Sandbox:WebhookSecret is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentStore>();
builder.Services.AddHttpClient("webhooks", http => http.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<WebhookSender>();
builder.Services.AddHostedService(services => services.GetRequiredService<WebhookSender>());

var app = builder.Build();

var api = app.MapGroup("/v1").AddEndpointFilter(async (context, next) =>
{
    var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<SandboxOptions>>().Value.ApiKey!;
    var given = context.HttpContext.Request.Headers.Authorization.ToString();
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes($"Bearer {expected}"), Encoding.UTF8.GetBytes(given))
        ? await next(context)
        : Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
});

api.MapPost("/payments", (CreatePayment body, HttpRequest request, PaymentStore store, IOptions<SandboxOptions> options, TimeProvider clock) =>
{
    if (body.Amount <= 0 || string.IsNullOrWhiteSpace(body.Reference) || !Uri.TryCreate(body.ReturnUrl, UriKind.Absolute, out _))
    {
        return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var now = clock.GetUtcNow();
    var key = request.Headers["Idempotency-Key"].FirstOrDefault();
    var payment = store.Create(key, id => new Payment
    {
        Id = id,
        Reference = body.Reference,
        Amount = body.Amount,
        Currency = body.Currency ?? "TRY",
        Description = body.Description ?? string.Empty,
        ReturnUrl = body.ReturnUrl,
        ExpiresAt = body.ExpiresAt ?? now.AddMinutes(15),
        CreatedAt = now,
    });
    return Results.Json(View(payment, options.Value, now), statusCode: StatusCodes.Status201Created);
});

api.MapGet("/payments/{id}", (string id, PaymentStore store, IOptions<SandboxOptions> options, TimeProvider clock) =>
    store.Find(id) is { } payment ? Results.Json(View(payment, options.Value, clock.GetUtcNow())) : Results.Json(new { error = "not_found" }, statusCode: 404));

api.MapPost("/payments/{id}/refunds", (string id, HttpRequest request, PaymentStore store, WebhookSender webhooks, TimeProvider clock) =>
{
    if (store.Find(id) is not { } payment)
    {
        return Results.Json(new { error = "not_found" }, statusCode: 404);
    }

    if (payment.Status is not ("succeeded" or "refunded"))
    {
        return Results.Json(new { error = "not_refundable", status = payment.Status }, statusCode: StatusCodes.Status409Conflict);
    }

    var (refundId, created) = store.Refund(payment, request.Headers["Idempotency-Key"].FirstOrDefault());
    if (created)
    {
        webhooks.Send("payment.refunded", payment, clock.GetUtcNow());
    }

    return Results.Json(new { id = refundId, payment = payment.Id, status = "succeeded", amount = payment.Refunded }, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/checkout/{id}", (string id, PaymentStore store, IOptions<SandboxOptions> options, TimeProvider clock) =>
    store.Find(id) is { } payment
        ? Results.Content(CheckoutPage.Render(payment, options.Value.PublicUrl, clock.GetUtcNow()), "text/html; charset=utf-8")
        : Results.NotFound());

app.MapPost("/checkout/{id}/pay", (string id, PaymentStore store, WebhookSender webhooks, TimeProvider clock, bool notify = true) =>
    Decide(id, "succeeded", notify, store, webhooks, clock));

app.MapPost("/checkout/{id}/decline", (string id, PaymentStore store, WebhookSender webhooks, TimeProvider clock) =>
    Decide(id, "failed", true, store, webhooks, clock));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

static IResult Decide(string id, string outcome, bool notify, PaymentStore store, WebhookSender webhooks, TimeProvider clock)
{
    var now = clock.GetUtcNow();
    if (store.Find(id) is not { } payment)
    {
        return Results.NotFound();
    }

    if (payment.StatusAt(now) == "pending" && store.Transition(payment, "pending", outcome) && notify)
    {
        webhooks.Send(outcome == "succeeded" ? "payment.succeeded" : "payment.failed", payment, now);
    }

    return Results.Redirect(payment.ReturnUrl, permanent: false, preserveMethod: false);
}

static object View(Payment payment, SandboxOptions options, DateTimeOffset now) => new
{
    id = payment.Id,
    reference = payment.Reference,
    status = payment.StatusAt(now),
    amount = payment.Amount,
    currency = payment.Currency,
    refunded = payment.Refunded,
    expiresAt = payment.ExpiresAt,
    checkoutUrl = $"{options.PublicUrl.TrimEnd('/')}/checkout/{Uri.EscapeDataString(payment.Id)}",
};

internal sealed record CreatePayment(string Reference, long Amount, string? Currency, string? Description, string ReturnUrl, DateTimeOffset? ExpiresAt);
