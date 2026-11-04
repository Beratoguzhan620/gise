namespace Gise.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record OrganizationDto(Guid Id, string Name, string Role, int Members, int Venues, int Events, DateTimeOffset CreatedAt);

public sealed record MeDto(UserDto User, List<OrganizationDto> Organizations);

public sealed record MemberDto(Guid UserId, string DisplayName, string Email, string Role, DateTimeOffset JoinedAt);

public sealed record RowDto(string Label, int Seats);

public sealed record SectionDto(string Key, string Name, string Kind, int Places, List<RowDto> Rows, int Capacity);

public sealed record VenueDto(Guid Id, string Name, string City, string Address, int Capacity, List<SectionDto> Sections, int Events, DateTimeOffset CreatedAt);

public sealed record CategoryDto(Guid Id, string Name, decimal Price);

public sealed record SectionPriceDto(string Section, Guid CategoryId);

public sealed record InventoryDto(int Available, int Held, int Sold);

public sealed record OrganizerEventDto(
    Guid Id, Guid OrganizationId, Guid VenueId, string Title, string Description, string VenueName, string City, string Address,
    DateTimeOffset StartsAt, DateTimeOffset SaleStartsAt, string Status, string SaleState, int MaxTicketsPerBuyer, int? RefundCutoffHours,
    bool WaitingRoom, int AdmitPerMinute, string Currency, List<CategoryDto> Categories, List<SectionPriceDto> Pricing, List<SectionDto> Layout,
    int Capacity, InventoryDto Inventory, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, DateTimeOffset? CancelledAt);

public sealed record EventSummaryDto(
    Guid Id, string Title, string Organizer, string VenueName, string City, DateTimeOffset StartsAt, DateTimeOffset SaleStartsAt,
    string SaleState, decimal PriceFrom, string Currency, int Available, int Capacity, bool WaitingRoom);

public sealed record PageDto<T>(List<T> Items, int Total, int PageNumber, int PageSize);

public sealed record PublicSectionDto(string Key, string Name, string Kind, Guid CategoryId, int Places, List<RowDto> Rows, int Capacity);

public sealed record EventDetailDto(
    Guid Id, string Title, string Description, string Organizer, string VenueName, string City, string Address, DateTimeOffset StartsAt,
    DateTimeOffset SaleStartsAt, string SaleState, int MaxTicketsPerBuyer, DateTimeOffset? RefundDeadline, bool WaitingRoom, string Currency,
    List<CategoryDto> Categories, List<PublicSectionDto> Sections, int Available, int Capacity);

public sealed record RowAvailabilityDto(string Label, string Seats);

public sealed record SectionAvailabilityDto(string Key, string Kind, int Available, int Places, List<RowAvailabilityDto> Rows);

public sealed record AvailabilityDto(Guid EventId, string SaleState, int Available, List<SectionAvailabilityDto> Sections, DateTimeOffset CheckedAt);
