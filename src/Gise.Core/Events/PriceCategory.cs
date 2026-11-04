namespace Gise.Core.Events;

/// <summary>A price level, for example "Front stalls" at 1,250.00 TRY. Sections are priced by naming a category.</summary>
public sealed class PriceCategory
{
    public const int NameMaxLength = 40;
    public const int MaxPerEvent = 8;
    public const decimal MinPrice = 1m;
    public const decimal MaxPrice = 100_000m;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Where it is listed, from 0; categories are shown in the order the organizer gave them.</summary>
    public int Position { get; set; }

    /// <summary>In kuruş (1/100 TRY), so sums are exact.</summary>
    public long PriceMinor { get; set; }
}

/// <summary>Which category a section's places are sold in.</summary>
public sealed class SectionPrice
{
    public string SectionKey { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }
}

public static class Money
{
    public const string Currency = "TRY";

    public static long ToMinor(decimal amount) => decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    public static decimal FromMinor(long minor) => minor / 100m;

    public static bool HasCents(decimal amount) => decimal.Round(amount, 2) == amount;
}
