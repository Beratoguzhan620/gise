using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Gise.Api.Features.Demo;

public sealed record DemoAccount(string Role, string Email, string Password);

internal static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo", Accounts)
            .WithTags("Auth")
            .WithSummary("The public demo's sign-ins")
            .WithDescription("Answers 404 unless this installation is the public demo.");
        return app;
    }

    private static Results<Ok<List<DemoAccount>>, NotFound> Accounts(IOptions<DemoOptions> options) =>
        options.Value is { Enabled: true } demo
            ? TypedResults.Ok(new List<DemoAccount>
            {
                new("Buyer", demo.BuyerEmail, demo.Password),
                new("Organizer", demo.OrganizerEmail, demo.Password),
            })
            : TypedResults.NotFound();
}
