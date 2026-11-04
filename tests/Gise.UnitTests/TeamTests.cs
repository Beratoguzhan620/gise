using Gise.Core;
using Gise.Core.Organizations;

namespace Gise.UnitTests;

public sealed class TeamTests
{
    [Theory]
    [InlineData(Role.Staff, Role.Staff, true)]
    [InlineData(Role.Staff, Role.Manager, false)]
    [InlineData(Role.Manager, Role.Manager, true)]
    [InlineData(Role.Manager, Role.Owner, false)]
    [InlineData(Role.Owner, Role.Manager, true)]
    public void Each_role_includes_the_ones_before_it(Role role, Role needed, bool allowed) =>
        Assert.Equal(allowed, Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, Builders.Now).Can(needed));

    [Fact]
    public void An_organizer_starts_with_its_creator_as_owner()
    {
        var (organization, owner) = Organization.Create("  Karadeniz   Sahne ", Guid.NewGuid(), Builders.Now);

        Assert.Equal("Karadeniz Sahne", organization.Name);
        Assert.Equal((organization.Id, Role.Owner), (owner.OrganizationId, owner.Role));
    }

    [Fact]
    public void A_team_keeps_an_owner()
    {
        Organization.EnsureAnOwnerRemains(1);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => Organization.EnsureAnOwnerRemains(0)).Code);
    }

    [Fact]
    public void Unknown_roles_are_refused() =>
        Assert.Throws<DomainException>(() => Membership.Create(Guid.NewGuid(), Guid.NewGuid(), (Role)7, Builders.Now));
}
