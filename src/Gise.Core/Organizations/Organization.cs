namespace Gise.Core.Organizations;

/// <summary>Whoever puts on events: a venue, a festival, a theatre company. Its team sells and checks the tickets.</summary>
public sealed class Organization
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 80;

    private Organization()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A new organizer and its first owner.</summary>
    public static (Organization Organization, Membership Owner) Create(string name, Guid ownerId, DateTimeOffset now)
    {
        var organization = new Organization { Id = Guid.CreateVersion7(now), Name = Text.Clean(name, NameMinLength, NameMaxLength, "An organizer's name"), CreatedBy = ownerId, CreatedAt = now };
        return (organization, Membership.Create(organization.Id, ownerId, Role.Owner, now));
    }

    public void Rename(string name) => Name = Text.Clean(name, NameMinLength, NameMaxLength, "An organizer's name");

    /// <summary>
    /// Checks that a change to the team leaves somebody who can manage it.
    /// <paramref name="ownersAfter"/> is how many owners there would be after the change.
    /// </summary>
    public static void EnsureAnOwnerRemains(int ownersAfter)
    {
        if (ownersAfter < 1)
        {
            throw new DomainException(ErrorCodes.InvalidState, "An organizer needs at least one owner. Make someone else an owner first.");
        }
    }
}
