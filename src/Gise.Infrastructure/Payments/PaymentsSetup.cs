using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gise.Infrastructure.Payments;

public static class PaymentsSetup
{
    public static IServiceCollection AddGisePayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.Section))
            .Validate(o => o.Provider == SandboxGateway.ProviderName, "Payments:Provider must be Sandbox; other providers plug in behind IPaymentGateway.")
            .Validate(o => o.PaymentMinutes is >= 2 and <= 60, "Payments:PaymentMinutes must be between 2 and 60.")
            .Validate(o => o.ReturnUrl.Contains("{orderId}", StringComparison.Ordinal), "Payments:ReturnUrl must contain {orderId}.")
            .ValidateOnStart();

        services.AddHttpClient<IPaymentGateway, SandboxGateway>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<PaymentOptions>>().Value;
            if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl))
            {
                http.BaseAddress = baseUrl;
            }

            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddSingleton<IWebhookVerifier, SandboxWebhookVerifier>();
        return services;
    }
}
