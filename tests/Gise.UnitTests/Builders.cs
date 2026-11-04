using Gise.Core.Events;
using Gise.Core.Venues;

namespace Gise.UnitTests;

internal static class Builders
{
    public static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid Organizer = Guid.NewGuid();

    public static List<Section> HallSections() =>
    [
        new() { Key = "A", Name = "Parter", Kind = SectionKind.Seated, Rows = [new() { Label = "A", Seats = 10 }, new() { Label = "B", Seats = 12 }] },
        new() { Key = "floor", Name = "Ayakta", Kind = SectionKind.Standing, Capacity = 50 },
    ];

    public static Venue Hall() => Venue.Create(Organizer, "Kültür Merkezi", "Samsun", null, HallSections(), Now);

    public static EventDetails Details(
        DateTimeOffset? startsAt = null,
        DateTimeOffset? saleStartsAt = null,
        int maxTickets = 6,
        int? refundCutoffHours = 48,
        bool waitingRoom = false,
        int admitPerMinute = 0,
        IReadOnlyList<(string, decimal)>? categories = null,
        IReadOnlyList<(string, string)>? pricing = null) => new(
            "Akustik Gece",
            "Bir akşam.",
            startsAt ?? Now.AddDays(10),
            saleStartsAt ?? Now,
            maxTickets,
            refundCutoffHours,
            waitingRoom,
            admitPerMinute,
            categories ?? [("Ön", 750m), ("Ayakta", 300m)],
            pricing ?? [("A", "Ön"), ("floor", "Ayakta")]);
}
