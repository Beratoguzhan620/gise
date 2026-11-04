namespace Gise.Core.Events;

public enum PlaceStatus
{
    Available = 0,

    /// <summary>In someone's basket until <see cref="Place.HeldUntil"/>; afterwards it counts as available again.</summary>
    Held = 1,

    Sold = 2,
}

/// <summary>
/// One seat, or one spot in a standing area, for one event. Every sale goes through a conditional update of
/// these rows, so a place is never sold twice however many people ask for it at once.
/// </summary>
public sealed class Place
{
    private Place()
    {
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public string SectionKey { get; private set; } = string.Empty;

    /// <summary>The row label; <c>null</c> in a standing area.</summary>
    public string? Row { get; private set; }

    /// <summary>The seat number in its row, or the spot's number in a standing area.</summary>
    public int Number { get; private set; }

    public Guid CategoryId { get; private set; }

    public PlaceStatus Status { get; private set; }

    public Guid? HoldId { get; private set; }

    public DateTimeOffset? HeldUntil { get; private set; }

    public bool IsAvailable(DateTimeOffset now) =>
        Status == PlaceStatus.Available || (Status == PlaceStatus.Held && HeldUntil <= now);

    /// <summary>One place for each seat and standing spot in the event's floor plan.</summary>
    public static IEnumerable<Place> For(Event @event)
    {
        foreach (var section in @event.Layout)
        {
            var category = @event.CategoryOf(section.Key).Id;
            if (section.Kind == Venues.SectionKind.Standing)
            {
                for (var number = 1; number <= section.Capacity; number++)
                {
                    yield return New(@event.Id, section.Key, null, number, category);
                }

                continue;
            }

            foreach (var row in section.Rows)
            {
                for (var number = 1; number <= row.Seats; number++)
                {
                    yield return New(@event.Id, section.Key, row.Label, number, category);
                }
            }
        }
    }

    private static Place New(Guid eventId, string section, string? row, int number, Guid category) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        SectionKey = section,
        Row = row,
        Number = number,
        CategoryId = category,
        Status = PlaceStatus.Available,
    };
}
