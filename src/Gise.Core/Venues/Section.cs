namespace Gise.Core.Venues;

public enum SectionKind
{
    /// <summary>Numbered seats in rows; buyers pick their seats.</summary>
    Seated = 0,

    /// <summary>A standing area with a capacity; buyers pick how many.</summary>
    Standing = 1,
}

/// <summary>One part of a venue's floor plan, in the order it is drawn (nearest the stage first).</summary>
public sealed class Section
{
    public const int KeyMaxLength = 12;
    public const int NameMaxLength = 40;
    public const int MaxRows = 60;
    public const int MaxSeatsPerRow = 80;
    public const int MaxCapacity = 20_000;

    /// <summary>Short and stable, for example <c>A</c> or <c>floor</c>; places and prices refer to it.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public SectionKind Kind { get; set; }

    /// <summary>Seated sections: the rows from the front.</summary>
    public List<SeatRow> Rows { get; set; } = [];

    /// <summary>Standing sections: how many people fit.</summary>
    public int Capacity { get; set; }

    public int Places => Kind == SectionKind.Seated ? Rows.Sum(r => r.Seats) : Capacity;
}

public sealed class SeatRow
{
    public const int LabelMaxLength = 4;

    public string Label { get; set; } = string.Empty;

    /// <summary>Seats are numbered 1 to this, left to right as seen from the stage.</summary>
    public int Seats { get; set; }
}
