namespace Gise.Core.Events;

public enum EventStatus
{
    /// <summary>Being prepared; only the organizer sees it.</summary>
    Draft = 0,

    /// <summary>Public; tickets sell from the sale start until the event starts.</summary>
    Published = 1,

    /// <summary>Called off; nothing sells and paid orders are refunded.</summary>
    Cancelled = 2,
}

/// <summary>What a buyer can do right now, worked out from the status, the clock and what is left.</summary>
public enum SaleState
{
    Draft = 0,
    Upcoming = 1,
    OnSale = 2,
    SoldOut = 3,
    Ended = 4,
    Cancelled = 5,
}
