using Gise.Infrastructure.Mail;
using Gise.Infrastructure.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gise.Infrastructure;

public static class TicketsSetup
{
    public static IServiceCollection AddGiseTickets(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TicketOptions>().Bind(configuration.GetSection(TicketOptions.Section));
        services.AddSingleton<TicketCodes>();
        services.AddOptions<MailOptions>()
            .Bind(configuration.GetSection(MailOptions.Section))
            .Validate(o => o.PollSeconds >= 0 && o.MaxAttempts >= 1, "Mail:PollSeconds cannot be negative and Mail:MaxAttempts must be at least 1.")
            .ValidateOnStart();
        services.AddSingleton<IMailTransport, SmtpMailTransport>();
        return services;
    }
}
