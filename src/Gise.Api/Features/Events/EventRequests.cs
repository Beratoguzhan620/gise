using Gise.Api.Features.Venues;
using Gise.Core.Events;

namespace Gise.Api.Features.Events;

public sealed record CategoryRequest(string Name, decimal Price);

public sealed record SectionPriceRequest(string Section, string Category);

public sealed record EventRequest(
    Guid VenueId,
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset SaleStartsAt,
    List<CategoryRequest> Categories,
    List<SectionPriceRequest> Pricing,
    int MaxTicketsPerBuyer = 6,
    int? RefundCutoffHours = 48,
    bool WaitingRoom = false,
    int AdmitPerMinute = 0)
{
    public EventDetails ToDetails() => new(
        Title,
        Description,
        StartsAt,
        SaleStartsAt,
        MaxTicketsPerBuyer,
        RefundCutoffHours,
        WaitingRoom,
        AdmitPerMinute,
        (Categories ?? []).Select(c => (c.Name, c.Price)).ToList(),
        (Pricing ?? []).Select(p => (p.Section, p.Category)).ToList());
}

public sealed record CategoryResponse(Guid Id, string Name, decimal Price);

public sealed record SectionPriceResponse(string Section, Guid CategoryId);

public sealed record OrganizerEventResponse(
    Guid Id,
    Guid OrganizationId,
    Guid VenueId,
    string Title,
    string Description,
    string VenueName,
    string City,
    string Address,
    DateTimeOffset StartsAt,
    DateTimeOffset SaleStartsAt,
    EventStatus Status,
    SaleState SaleState,
    int MaxTicketsPerBuyer,
    int? RefundCutoffHours,
    bool WaitingRoom,
    int AdmitPerMinute,
    string Currency,
    IReadOnlyList<CategoryResponse> Categories,
    IReadOnlyList<SectionPriceResponse> Pricing,
    IReadOnlyList<SectionResponse> Layout,
    int Capacity,
    InventoryCounts Inventory,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CancelledAt)
{
    public static OrganizerEventResponse From(Event e, InventoryCounts inventory, DateTimeOffset now) => new(
        e.Id,
        e.OrganizationId,
        e.VenueId,
        e.Title,
        e.Description,
        e.VenueName,
        e.City,
        e.Address,
        e.StartsAt,
        e.SaleStartsAt,
        e.Status,
        e.SaleStateAt(now, e.Status == EventStatus.Published && inventory.Available == 0),
        e.MaxTicketsPerBuyer,
        e.RefundCutoffHours,
        e.WaitingRoom,
        e.AdmitPerMinute,
        Money.Currency,
        CategoriesOf(e),
        e.Pricing.Select(p => new SectionPriceResponse(p.SectionKey, p.CategoryId)).ToList(),
        VenueEndpoints.ToSections(e.Layout),
        e.Capacity,
        inventory,
        e.CreatedAt,
        e.PublishedAt,
        e.CancelledAt);

    internal static IReadOnlyList<CategoryResponse> CategoriesOf(Event e) =>
        e.Categories.OrderBy(c => c.Position).Select(c => new CategoryResponse(c.Id, c.Name, Money.FromMinor(c.PriceMinor))).ToList();
}
