using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Gise.IntegrationTests.Support;

namespace Gise.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class GateTests(ApiFactory factory)
{
    [Fact]
    public async Task The_gate_package_lets_a_device_check_signatures_on_its_own()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync("Selin Yıldız");
        using var staff = await factory.RegisterAsync("Kapı");
        await organizer.AddMemberAsync(staff, "Staff");
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Seats("B-A-2"));
        var code = (await buyer.CodesAsync(order.Id)).Single();

        var package = await (await staff.Http.GetAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/gate")).ReadAsync<GatePackageDto>();

        var ticket = Assert.Single(package.Tickets);
        Assert.Equal(("Balkon · Row A · Seat 2", "Selin Yıldız", "Valid"), (ticket.Label, ticket.Holder, ticket.Status));
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(package.PublicKey), out _);
        var parts = code["GS1.".Length..].Split('.');
        var payload = Base64Url.DecodeFromChars(parts[0]);
        Assert.True(key.VerifyData(payload, Base64Url.DecodeFromChars(parts[1]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal(ticket.Id, new Guid(payload.AsSpan(5, 16), bigEndian: true));
        Assert.Equal(package.KeyId, Convert.ToHexStringLower(payload.AsSpan(1, 4)));
    }

    [Fact]
    public async Task A_ticket_gets_in_once_and_the_second_try_says_when_and_where()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Standing(1));
        var code = (await buyer.CodesAsync(order.Id)).Single();

        var first = await organizer.ScanAsync(@event.Id, code, "Kuzey");
        var second = await organizer.ScanAsync(@event.Id, code, "Güney");

        Assert.Equal(("Admitted", "Ayakta"), (first.Outcome, first.Ticket!.Label));
        Assert.Equal(("AlreadyUsed", "Kuzey"), (second.Outcome, second.FirstGate));
        Assert.Equal(first.ScannedAt, second.FirstCheckedInAt);
    }

    [Fact]
    public async Task Forged_damaged_and_other_events_tickets_are_turned_away()
    {
        var organizer = await factory.OrganizerAsync();
        var tonight = await organizer.PublishedAsync(factory);
        var tomorrow = await organizer.PublishedAsync(factory, TestApi.EventBody(organizer.VenueId, factory.Clock.GetUtcNow(), title: "Yarın"));
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, tomorrow.Id, Buying.Standing(1));
        var code = (await buyer.CodesAsync(order.Id)).Single();
        var tampered = code[..^3] + (code[^3] == 'A' ? "B" : "A") + code[^2..];
        var otherKey = Forge(code);

        var results = await organizer.SyncAsync(tonight.Id,
        [
            new { scanId = Guid.NewGuid(), code, gate = "Kuzey" },
            new { scanId = Guid.NewGuid(), code = tampered, gate = "Kuzey" },
            new { scanId = Guid.NewGuid(), code = otherKey, gate = "Kuzey" },
            new { scanId = Guid.NewGuid(), code = "https://example.com/not-a-ticket", gate = "Kuzey" },
        ]);

        Assert.Equal(["WrongEvent", "Invalid", "Invalid", "Invalid"], results.Select(r => r.Outcome));
        Assert.Equal("Admitted", (await organizer.ScanAsync(tomorrow.Id, code, "Kuzey")).Outcome);
    }

    [Fact]
    public async Task Syncing_the_same_backlog_twice_counts_each_scan_once()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Standing(3));
        var codes = await buyer.CodesAsync(order.Id);
        var at = factory.Clock.GetUtcNow().AddMinutes(-5);
        var backlog = codes.Select((code, i) => (object)new { scanId = Guid.NewGuid(), code, gate = "Güney", device = "tablet-2", scannedAt = at.AddSeconds(i) }).ToArray();

        var first = await organizer.SyncAsync(@event.Id, backlog);
        var again = await organizer.SyncAsync(@event.Id, backlog);

        Assert.All(first, r => Assert.Equal("Admitted", r.Outcome));
        Assert.Equal(first.Select(r => (r.ScanId, r.Outcome)), again.Select(r => (r.ScanId, r.Outcome)));
        Assert.Equal(3, await factory.ScalarAsync<long>("SELECT count(*) FROM check_ins WHERE event_id = @p0", @event.Id));
    }

    [Fact]
    public async Task Two_offline_gates_that_let_the_same_ticket_in_are_reported_with_the_earliest_scan_kept()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync("Kopyalayan");
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Seats("A-B-1"));
        var code = (await buyer.CodesAsync(order.Id)).Single();
        var now = factory.Clock.GetUtcNow();

        // The north gate syncs first, but the south gate scanned the same ticket five seconds earlier.
        var north = await organizer.SyncAsync(@event.Id, [new { scanId = Guid.NewGuid(), code, gate = "Kuzey", device = "tablet-1", scannedAt = now.AddSeconds(-10) }]);
        var south = await organizer.SyncAsync(@event.Id, [new { scanId = Guid.NewGuid(), code, gate = "Güney", device = "tablet-2", scannedAt = now.AddSeconds(-15) }]);
        var summary = await (await organizer.Owner.Http.GetAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/check-ins")).ReadAsync<CheckInSummaryDto>();

        Assert.Equal("Admitted", north.Single().Outcome);
        Assert.Equal(("AlreadyUsed", "Güney"), (south.Single().Outcome, south.Single().FirstGate));
        var repeat = Assert.Single(summary.Repeats);
        Assert.Equal(("Parter · Row B · Seat 1", "Güney", "Güney", "tablet-2"), (repeat.Label, repeat.FirstGate, repeat.AgainGate, repeat.AgainDevice));
        Assert.Equal((1, 1, 0), (summary.Tickets, summary.Admitted, summary.Remaining));
        Assert.Equal("Güney", Assert.Single(summary.Gates).Gate);
    }

    [Fact]
    public async Task The_summary_counts_who_is_in_at_each_gate()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var buyer = await factory.RegisterAsync();
        var order = await factory.BuyAsync(buyer, @event.Id, Buying.Standing(4));
        var codes = await buyer.CodesAsync(order.Id);
        await organizer.ScanAsync(@event.Id, codes[0], "Kuzey");
        await organizer.ScanAsync(@event.Id, codes[1], "Kuzey");
        await organizer.ScanAsync(@event.Id, codes[2], "Güney");
        await organizer.ScanAsync(@event.Id, "GS1.garbage.garbage", "Güney");

        var summary = await (await organizer.Owner.Http.GetAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/check-ins")).ReadAsync<CheckInSummaryDto>();

        Assert.Equal((4, 3, 1, 1), (summary.Tickets, summary.Admitted, summary.Remaining, summary.Rejected));
        Assert.Equal([("Kuzey", 2), ("Güney", 1)], summary.Gates.Select(g => (g.Gate, g.Admitted)));
        Assert.Equal(4, summary.Recent.Count);
    }

    [Fact]
    public async Task Only_the_organizers_team_works_the_gate()
    {
        var organizer = await factory.OrganizerAsync();
        var @event = await organizer.PublishedAsync(factory);
        using var outsider = await factory.RegisterAsync("Dışarıdan");

        var package = await outsider.Http.GetAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/gate");
        var scan = await outsider.Http.PostAsync($"/api/organizations/{organizer.Id}/events/{@event.Id}/check-ins",
            System.Net.Http.Json.JsonContent.Create(new { scans = new[] { new { scanId = Guid.NewGuid(), code = "x", gate = "Kuzey" } } }));

        Assert.Equal(HttpStatusCode.NotFound, package.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scan.StatusCode);
    }

    // The same payload signed with a key that is not ours.
    private static string Forge(string code)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Base64Url.DecodeFromChars(code["GS1.".Length..].Split('.')[0]);
        return $"GS1.{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))}";
    }
}
