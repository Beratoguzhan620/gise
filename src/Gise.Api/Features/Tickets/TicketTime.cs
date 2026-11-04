using Gise.Infrastructure.Tickets;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Tickets;

/// <summary>Event times in the zone people attend them in, for tickets and emails.</summary>
internal sealed class TicketTime(IOptions<TicketOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.TryFindSystemTimeZoneById(options.Value.TimeZone, out var zone)
        ? zone
        : TimeZoneInfo.CreateCustomTimeZone(options.Value.TimeZone, TimeSpan.FromHours(3), options.Value.TimeZone, options.Value.TimeZone);

    public DateTimeOffset Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _zone);
}
