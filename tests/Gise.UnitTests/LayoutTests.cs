using Gise.Core;
using Gise.Core.Venues;

namespace Gise.UnitTests;

public sealed class LayoutTests
{
    [Fact]
    public void A_venue_counts_seats_and_standing_spots()
    {
        var venue = Builders.Hall();

        Assert.Equal(10 + 12 + 50, venue.Capacity);
        Assert.Equal([22, 50], venue.Sections.Select(s => s.Places));
    }

    [Fact]
    public void Keys_and_labels_are_trimmed_and_standing_areas_drop_rows()
    {
        var sections = Layout.Check(
        [
            new Section { Key = " A ", Name = "  Parter   önü ", Kind = SectionKind.Seated, Rows = [new() { Label = " 1 ", Seats = 4 }] },
            new Section { Key = "floor", Name = "Ayakta", Kind = SectionKind.Standing, Capacity = 9, Rows = [new() { Label = "X", Seats = 3 }] },
        ]);

        Assert.Equal(("A", "Parter önü", "1"), (sections[0].Key, sections[0].Name, sections[0].Rows[0].Label));
        Assert.Empty(sections[1].Rows);
        Assert.Equal(9, sections[1].Places);
    }

    [Theory]
    [InlineData("A", "a")]
    [InlineData("floor", "FLOOR")]
    public void Section_keys_are_unique_whatever_their_case(string first, string second)
    {
        var error = Assert.Throws<DomainException>(() => Layout.Check(
        [
            new Section { Key = first, Name = "Bir", Kind = SectionKind.Standing, Capacity = 5 },
            new Section { Key = second, Name = "İki", Kind = SectionKind.Standing, Capacity = 5 },
        ]));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void A_floor_plan_has_at_most_fifty_thousand_places()
    {
        Section Area(string key, int capacity) => new() { Key = key, Name = key, Kind = SectionKind.Standing, Capacity = capacity };

        Assert.Equal(50_000, Layout.Check([Area("N", 20_000), Area("S", 20_000), Area("E", 10_000)]).Sum(s => s.Places));
        Assert.Throws<DomainException>(() => Layout.Check([Area("N", 20_000), Area("S", 20_000), Area("E", 10_001)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(81)]
    public void Rows_have_one_to_eighty_seats(int seats) =>
        Assert.Throws<DomainException>(() => Layout.Check(
            [new Section { Key = "A", Name = "Parter", Kind = SectionKind.Seated, Rows = [new() { Label = "A", Seats = seats }] }]));

    [Fact]
    public void A_seated_section_needs_rows() =>
        Assert.Throws<DomainException>(() => Layout.Check([new Section { Key = "A", Name = "Parter", Kind = SectionKind.Seated }]));
}
