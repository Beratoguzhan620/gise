namespace Gise.Api.Features.Demo;

/// <summary>
/// The public demo: sample events that are always on sale, and two sign-ins it publishes (an organizer and a buyer).
/// Off everywhere else.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Where this API listens, for the seeder's own calls.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    public string OrganizerEmail { get; set; } = "organizer@gise.demo";

    public string BuyerEmail { get; set; } = "buyer@gise.demo";

    /// <summary>Shared by both demo sign-ins; published on the sign-in page, so it guards nothing.</summary>
    public string Password { get; set; } = "gise-demo-password";
}
