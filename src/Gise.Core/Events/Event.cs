using Gise.Core.Venues;

namespace Gise.Core.Events;

/// <summary>What an organizer fills in for an event. Prices name categories; every section of the venue gets one.</summary>
public sealed record EventDetails(
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset SaleStartsAt,
    int MaxTicketsPerBuyer,
    int? RefundCutoffHours,
    bool WaitingRoom,
    int AdmitPerMinute,
    IReadOnlyList<(string Name, decimal Price)> Categories,
    IReadOnlyList<(string SectionKey, string Category)> Pricing);

public sealed class Event
{
    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 4000;
    public const int MaxTicketsLimit = 10;
    public const int MaxRefundCutoffHours = 24 * 30;
    public const int MinAdmitPerMinute = 10;
    public const int MaxAdmitPerMinute = 10_000;

    private Event()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid VenueId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string VenueName { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset SaleStartsAt { get; private set; }

    public EventStatus Status { get; private set; }

    /// <summary>The most tickets one account may hold or own for this event.</summary>
    public int MaxTicketsPerBuyer { get; private set; }

    /// <summary>Buyers may refund until this many hours before the start; <c>null</c> means only the organizer refunds.</summary>
    public int? RefundCutoffHours { get; private set; }

    /// <summary>When on, buyers join a queue and are let in at <see cref="AdmitPerMinute"/>.</summary>
    public bool WaitingRoom { get; private set; }

    public int AdmitPerMinute { get; private set; }

    public List<PriceCategory> Categories { get; private set; } = [];

    public List<SectionPrice> Pricing { get; private set; } = [];

    /// <summary>The venue's floor plan as it was when the event went on sale.</summary>
    public List<Section> Layout { get; private set; } = [];

    public int Capacity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public static Event CreateDraft(Guid organizationId, Venue venue, EventDetails details, DateTimeOffset now)
    {
        var @event = new Event { Id = Guid.CreateVersion7(now), OrganizationId = organizationId, Status = EventStatus.Draft, CreatedAt = now };
        @event.Apply(venue, details, now);
        return @event;
    }

    public void Update(Venue venue, EventDetails details, DateTimeOffset now)
    {
        EnsureDraft("changed");
        Apply(venue, details, now);
    }

    /// <summary>
    /// Puts the event on sale with the venue's current floor plan. Returns the places to create, one per seat or standing spot.
    /// </summary>
    public IReadOnlyList<Place> Publish(Venue venue, DateTimeOffset now)
    {
        EnsureDraft("published");
        if (StartsAt <= now)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The event's start time has passed. Move it to the future first.");
        }

        CopyVenue(venue);
        CheckPricing(venue.Sections, Categories, Pricing);
        Layout = venue.Sections.Select(Copy).ToList();
        Capacity = Layout.Sum(s => s.Places);
        Status = EventStatus.Published;
        PublishedAt = now;
        return Place.For(this).ToList();
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
        {
            throw new DomainException(ErrorCodes.InvalidState, Status == EventStatus.Draft
                ? "A draft has not been announced; delete it instead."
                : "This event is already cancelled.");
        }

        if (StartsAt <= now)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The event has already started.");
        }

        Status = EventStatus.Cancelled;
        CancelledAt = now;
    }

    public void EnsureDraft(string action)
    {
        if (Status != EventStatus.Draft)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"Only a draft can be {action}; this event is {Status.ToString().ToLowerInvariant()}.");
        }
    }

    public SaleState SaleStateAt(DateTimeOffset now, bool soldOut) => Status switch
    {
        EventStatus.Draft => SaleState.Draft,
        EventStatus.Cancelled => SaleState.Cancelled,
        _ when now >= StartsAt => SaleState.Ended,
        _ when now < SaleStartsAt => SaleState.Upcoming,
        _ when soldOut => SaleState.SoldOut,
        _ => SaleState.OnSale,
    };

    /// <summary>The latest moment a buyer may refund on their own, or <c>null</c> when they may not.</summary>
    public DateTimeOffset? RefundDeadline => RefundCutoffHours is { } hours ? StartsAt.AddHours(-hours) : null;

    public PriceCategory CategoryOf(string sectionKey)
    {
        var id = Pricing.Single(p => string.Equals(p.SectionKey, sectionKey, StringComparison.Ordinal)).CategoryId;
        return Categories.Single(c => c.Id == id);
    }

    private void Apply(Venue venue, EventDetails details, DateTimeOffset now)
    {
        if (venue.OrganizationId != OrganizationId)
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no venue with this id among the organizer's.");
        }

        var title = Text.Clean(details.Title, TitleMinLength, TitleMaxLength, "A title");
        var description = Text.CleanParagraphs(details.Description, DescriptionMaxLength, "A description");
        if (details.StartsAt <= now)
        {
            throw Invalid("The event starts in the future.");
        }

        if (details.SaleStartsAt >= details.StartsAt)
        {
            throw Invalid("Sales start before the event does.");
        }

        if (details.MaxTicketsPerBuyer is < 1 or > MaxTicketsLimit)
        {
            throw Invalid($"A buyer may get 1 to {MaxTicketsLimit} tickets.");
        }

        if (details.RefundCutoffHours is < 0 or > MaxRefundCutoffHours)
        {
            throw Invalid($"Refunds close 0 to {MaxRefundCutoffHours} hours before the start, or are left to the organizer.");
        }

        if (details.WaitingRoom && details.AdmitPerMinute is < MinAdmitPerMinute or > MaxAdmitPerMinute)
        {
            throw Invalid($"A waiting room lets in {MinAdmitPerMinute} to {MaxAdmitPerMinute:N0} people a minute.");
        }

        var categories = CleanCategories(details.Categories, Categories);
        var pricing = CleanPricing(venue.Sections, categories, details.Pricing);

        Title = title;
        Description = description;
        VenueId = venue.Id;
        CopyVenue(venue);
        // Stored and compared in UTC whatever offset the organizer's browser sent.
        StartsAt = details.StartsAt.ToUniversalTime();
        SaleStartsAt = details.SaleStartsAt.ToUniversalTime();
        MaxTicketsPerBuyer = details.MaxTicketsPerBuyer;
        RefundCutoffHours = details.RefundCutoffHours;
        WaitingRoom = details.WaitingRoom;
        AdmitPerMinute = details.WaitingRoom ? details.AdmitPerMinute : 0;
        Categories = categories;
        Pricing = pricing;
        Capacity = venue.Capacity;
    }

    private void CopyVenue(Venue venue)
    {
        VenueName = venue.Name;
        City = venue.City;
        Address = venue.Address;
    }

    // Category ids survive edits by name, so links to them stay stable while a draft is reworked.
    private static List<PriceCategory> CleanCategories(IReadOnlyList<(string Name, decimal Price)> input, List<PriceCategory> existing)
    {
        if (input.Count is 0 or > PriceCategory.MaxPerEvent)
        {
            throw Invalid($"An event has 1 to {PriceCategory.MaxPerEvent} price categories.");
        }

        var categories = new List<PriceCategory>();
        foreach (var (name, price) in input)
        {
            var clean = Text.Clean(name, 1, PriceCategory.NameMaxLength, "A category name");
            if (price is < PriceCategory.MinPrice or > PriceCategory.MaxPrice || !Money.HasCents(price))
            {
                throw Invalid($"Category \"{clean}\" costs {PriceCategory.MinPrice:0.00} to {PriceCategory.MaxPrice:N2} TRY, to the kuruş.");
            }

            if (categories.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            {
                throw Invalid($"Two categories are called \"{clean}\".");
            }

            var id = existing.FirstOrDefault(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.NewGuid();
            categories.Add(new PriceCategory { Id = id, Name = clean, Position = categories.Count, PriceMinor = Money.ToMinor(price) });
        }

        return categories;
    }

    private static List<SectionPrice> CleanPricing(
        List<Section> sections, List<PriceCategory> categories, IReadOnlyList<(string SectionKey, string Category)> input)
    {
        var pricing = new List<SectionPrice>();
        foreach (var (sectionKey, categoryName) in input)
        {
            var section = sections.FirstOrDefault(s => string.Equals(s.Key, sectionKey?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw Invalid($"The venue has no section \"{sectionKey}\".");
            var category = categories.FirstOrDefault(c => string.Equals(c.Name, categoryName?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw Invalid($"Section \"{section.Key}\" names a category that does not exist: \"{categoryName}\".");
            if (pricing.Any(p => p.SectionKey == section.Key))
            {
                throw Invalid($"Section \"{section.Key}\" is priced twice.");
            }

            pricing.Add(new SectionPrice { SectionKey = section.Key, CategoryId = category.Id });
        }

        CheckPricing(sections, categories, pricing);
        return pricing;
    }

    private static void CheckPricing(List<Section> sections, List<PriceCategory> categories, List<SectionPrice> pricing)
    {
        var missing = sections.Where(s => pricing.All(p => p.SectionKey != s.Key)).Select(s => s.Key).ToList();
        if (missing.Count > 0)
        {
            throw Invalid($"Every section needs a price; missing: {string.Join(", ", missing)}.");
        }

        var unknown = pricing.Where(p => sections.All(s => s.Key != p.SectionKey)).Select(p => p.SectionKey).ToList();
        if (unknown.Count > 0)
        {
            throw Invalid($"The venue no longer has these sections: {string.Join(", ", unknown)}. Update the prices.");
        }

        var unused = categories.Where(c => pricing.All(p => p.CategoryId != c.Id)).Select(c => c.Name).ToList();
        if (unused.Count > 0)
        {
            throw Invalid($"No section uses these categories: {string.Join(", ", unused)}.");
        }
    }

    private static Section Copy(Section s) => new()
    {
        Key = s.Key,
        Name = s.Name,
        Kind = s.Kind,
        Capacity = s.Capacity,
        Rows = s.Rows.Select(r => new SeatRow { Label = r.Label, Seats = r.Seats }).ToList(),
    };

    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationFailed, message);
}
