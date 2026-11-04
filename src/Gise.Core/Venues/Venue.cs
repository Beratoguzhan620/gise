namespace Gise.Core.Venues;

/// <summary>A place where events happen, with its floor plan. Events copy the plan when they go on sale.</summary>
public sealed class Venue
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 80;
    public const int CityMaxLength = 60;
    public const int AddressMaxLength = 200;
    public const int MaxSections = 24;
    public const int MaxPlaces = 50_000;

    private Venue()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public List<Section> Sections { get; private set; } = [];

    public int Capacity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Venue Create(Guid organizationId, string name, string city, string? address, IEnumerable<Section> sections, DateTimeOffset now)
    {
        var venue = new Venue { Id = Guid.CreateVersion7(now), OrganizationId = organizationId, CreatedAt = now };
        venue.Update(name, city, address, sections);
        return venue;
    }

    public void Update(string name, string city, string? address, IEnumerable<Section> sections)
    {
        Name = Text.Clean(name, NameMinLength, NameMaxLength, "A venue's name");
        City = Text.Clean(city, 2, CityMaxLength, "A city");
        Address = string.IsNullOrWhiteSpace(address) ? string.Empty : Text.Clean(address, 1, AddressMaxLength, "An address");
        Sections = Layout.Check(sections);
        Capacity = Sections.Sum(s => s.Places);
    }
}

/// <summary>The rules every floor plan follows.</summary>
public static class Layout
{
    public static List<Section> Check(IEnumerable<Section>? sections)
    {
        var clean = (sections ?? []).Select(CleanSection).ToList();
        if (clean.Count is 0 or > Venue.MaxSections)
        {
            throw Invalid($"A floor plan has 1 to {Venue.MaxSections} sections.");
        }

        var duplicate = clean.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw Invalid($"Two sections have the key \"{duplicate.Key}\"; each key is used once.");
        }

        var places = clean.Sum(s => s.Places);
        return places <= Venue.MaxPlaces ? clean : throw Invalid($"A floor plan has at most {Venue.MaxPlaces:N0} places; this one has {places:N0}.");
    }

    private static Section CleanSection(Section section)
    {
        var key = (section.Key ?? string.Empty).Trim();
        if (key.Length is 0 or > Section.KeyMaxLength || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw Invalid($"A section key has 1 to {Section.KeyMaxLength} letters, digits, '-' or '_'.");
        }

        var name = Text.Clean(section.Name, 1, Section.NameMaxLength, $"Section \"{key}\"'s name");
        if (!Enum.IsDefined(section.Kind))
        {
            throw Invalid($"Section \"{key}\" is Seated or Standing.");
        }

        if (section.Kind == SectionKind.Standing)
        {
            return section.Capacity is >= 1 and <= Section.MaxCapacity
                ? new Section { Key = key, Name = name, Kind = SectionKind.Standing, Capacity = section.Capacity }
                : throw Invalid($"Standing section \"{key}\" holds 1 to {Section.MaxCapacity:N0} people.");
        }

        var rows = (section.Rows ?? []).Select(r => new SeatRow { Label = (r.Label ?? string.Empty).Trim(), Seats = r.Seats }).ToList();
        if (rows.Count is 0 or > Section.MaxRows)
        {
            throw Invalid($"Seated section \"{key}\" has 1 to {Section.MaxRows} rows.");
        }

        foreach (var row in rows)
        {
            if (row.Label.Length is 0 or > SeatRow.LabelMaxLength || !row.Label.All(char.IsLetterOrDigit))
            {
                throw Invalid($"Row labels in section \"{key}\" have 1 to {SeatRow.LabelMaxLength} letters or digits.");
            }

            if (row.Seats is < 1 or > Section.MaxSeatsPerRow)
            {
                throw Invalid($"Row {row.Label} in section \"{key}\" has 1 to {Section.MaxSeatsPerRow} seats.");
            }
        }

        if (rows.Select(r => r.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
        {
            throw Invalid($"Section \"{key}\" uses a row label twice.");
        }

        return new Section { Key = key, Name = name, Kind = SectionKind.Seated, Rows = rows };
    }

    private static DomainException Invalid(string message) => new(ErrorCodes.ValidationFailed, message);
}
