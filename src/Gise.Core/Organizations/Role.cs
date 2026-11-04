namespace Gise.Core.Organizations;

/// <summary>What a member may do for an organizer. Each role includes everything the ones before it can do.</summary>
public enum Role
{
    /// <summary>Checks tickets at the gate.</summary>
    Staff = 0,

    /// <summary>Also runs venues, events, sales and refunds.</summary>
    Manager = 1,

    /// <summary>Also manages the team.</summary>
    Owner = 2,
}
