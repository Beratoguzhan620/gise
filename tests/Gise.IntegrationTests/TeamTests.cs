using System.Net;
using System.Net.Http.Json;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TeamTests(ApiFactory factory)
{
    [Fact]
    public async Task Creating_an_organizer_makes_you_its_owner()
    {
        using var person = await factory.RegisterAsync();

        var created = await person.Http.PostAsJsonAsync("/api/organizations", new { name = "  Samsun   Caz Kulübü " });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var me = await (await person.Http.GetAsync("/api/me")).ReadAsync<MeDto>();
        var organization = Assert.Single(me.Organizations);
        Assert.Equal(("Samsun Caz Kulübü", "Owner", 1), (organization.Name, organization.Role, organization.Members));
    }

    [Fact]
    public async Task Staff_see_the_events_but_cannot_change_them()
    {
        var organizer = await factory.OrganizerAsync();
        using var staff = await factory.RegisterAsync("Kapı Görevlisi");
        await organizer.AddMemberAsync(staff, "Staff");

        Assert.Equal(HttpStatusCode.OK, (await staff.Http.GetAsync($"/api/organizations/{organizer.Id}/events")).StatusCode);
        var venue = await staff.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/venues", TestApi.Hall("Başka salon"));
        var @event = await staff.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/events", TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow()));

        Assert.Equal(HttpStatusCode.Forbidden, venue.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, @event.StatusCode);
        Assert.Equal("forbidden", await @event.ProblemCodeAsync());
    }

    [Fact]
    public async Task Managers_run_events_but_only_owners_manage_the_team()
    {
        var organizer = await factory.OrganizerAsync();
        using var manager = await factory.RegisterAsync("Müdür");
        using var newcomer = await factory.RegisterAsync("Yeni");
        await organizer.AddMemberAsync(manager, "Manager");

        var draft = await manager.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/events", TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow()));
        var add = await manager.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/members", new { email = newcomer.Email, role = "Staff" });

        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
    }

    [Fact]
    public async Task Outsiders_cannot_tell_an_organizer_exists()
    {
        var organizer = await factory.OrganizerAsync();
        using var outsider = await factory.RegisterAsync("Yabancı");

        var response = await outsider.Http.GetAsync($"/api/organizations/{organizer.Id}/events");
        var missing = await outsider.Http.GetAsync($"/api/organizations/{Guid.NewGuid()}/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((await response.Content.ReadAsStringAsync()).Split("traceId")[0], (await missing.Content.ReadAsStringAsync()).Split("traceId")[0]);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave_or_be_demoted()
    {
        var organizer = await factory.OrganizerAsync();
        var path = $"/api/organizations/{organizer.Id}/members/{organizer.Owner.Id}";

        var demote = await organizer.Owner.Http.PatchAsJsonAsync(path, new { role = "Manager" });
        var leave = await organizer.Owner.Http.DeleteAsync(path);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
    }

    [Fact]
    public async Task A_removed_member_loses_access_at_once()
    {
        var organizer = await factory.OrganizerAsync();
        using var manager = await factory.RegisterAsync("Eski Müdür");
        await organizer.AddMemberAsync(manager, "Manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.Http.GetAsync($"/api/organizations/{organizer.Id}/venues")).StatusCode);

        (await organizer.Owner.Http.DeleteAsync($"/api/organizations/{organizer.Id}/members/{manager.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await manager.Http.GetAsync($"/api/organizations/{organizer.Id}/venues")).StatusCode);
    }

    [Fact]
    public async Task Members_are_added_by_the_email_of_their_account()
    {
        var organizer = await factory.OrganizerAsync();

        var unknown = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/members", new { email = "nobody-here@example.com", role = "Staff" });
        using var staff = await factory.RegisterAsync("Gişe Görevlisi");
        await organizer.AddMemberAsync(staff, "Staff");
        var twice = await organizer.Owner.Http.PostAsJsonAsync($"/api/organizations/{organizer.Id}/members", new { email = staff.Email, role = "Manager" });
        var team = await (await organizer.Owner.Http.GetAsync($"/api/organizations/{organizer.Id}/members")).ReadAsync<List<MemberDto>>();

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(["Owner", "Staff"], team.Select(m => m.Role));
    }
}
