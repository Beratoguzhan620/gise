namespace Gise.Core.Queue;

/// <summary>
/// The fair order of a busy sale. Everyone who arrives before the sale opens gets a random place in line, so
/// refreshing at the right second earns nothing; whoever comes after opening queues behind them in arrival order.
/// The line moves at a fixed pace: the first <c>perMinute</c> people are let in when the sale opens, then
/// <c>perMinute</c> more every minute.
/// </summary>
public static class WaitingRoom
{
    /// <summary>Sorts the line: below 1 for early arrivals (random), from 1 up for later ones (seconds after opening).</summary>
    public static double OrderKey(DateTimeOffset joinedAt, DateTimeOffset saleStartsAt, Random random) =>
        joinedAt < saleStartsAt ? random.NextDouble() : 1 + (joinedAt - saleStartsAt).TotalSeconds;

    /// <summary>How many people from the front of the line are let in by <paramref name="now"/>.</summary>
    public static long AdmittedThrough(DateTimeOffset now, DateTimeOffset saleStartsAt, int perMinute) =>
        now < saleStartsAt ? 0 : perMinute + (long)Math.Floor(perMinute * (now - saleStartsAt).TotalSeconds / 60);

    /// <summary>When the person at <paramref name="position"/> (from 1) is let in.</summary>
    public static DateTimeOffset AdmittedAt(long position, DateTimeOffset saleStartsAt, int perMinute) =>
        position <= perMinute ? saleStartsAt : saleStartsAt.AddSeconds(Math.Ceiling((position - perMinute) * 60.0 / perMinute));
}

/// <summary>Someone's place in an event's waiting room.</summary>
public sealed class QueueEntry
{
    private QueueEntry()
    {
    }

    public Guid EventId { get; private set; }

    public Guid UserId { get; private set; }

    public double OrderKey { get; private set; }

    /// <summary>Breaks ties between people with the same key, in the order they arrived.</summary>
    public long Sequence { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>When this person was first seen at the front; their shopping time counts from here.</summary>
    public DateTimeOffset? AdmittedAt { get; private set; }

    public static QueueEntry Join(Guid eventId, Guid userId, DateTimeOffset saleStartsAt, DateTimeOffset now, Random random) =>
        new() { EventId = eventId, UserId = userId, OrderKey = WaitingRoom.OrderKey(now, saleStartsAt, random), JoinedAt = now };

    /// <summary>Back of the line, for someone whose shopping time ran out.</summary>
    public void Rejoin(DateTimeOffset saleStartsAt, DateTimeOffset now, Random random)
    {
        OrderKey = WaitingRoom.OrderKey(now, saleStartsAt, random);
        JoinedAt = now;
        AdmittedAt = null;
    }

    public void Admit(DateTimeOffset now) => AdmittedAt ??= now;
}
