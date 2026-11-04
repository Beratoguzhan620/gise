using System.Security.Claims;
using Gise.Api.Common;
using Gise.Api.Features.Events;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Gise.Api.Features.Sales;

internal static class QueueEndpoints
{
    public const string RateLimit = "queue";

    public static IEndpointRouteBuilder MapQueueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events/{eventId:guid}/queue").WithTags("Waiting room").RequireAuthorization();
        group.MapPost("/", Join).RequireRateLimiting(RateLimit).WithSummary("Join the waiting room, or see where you are")
            .WithDescription("""
                Everyone who joins before the sale opens gets a random place in line; later arrivals queue behind them in
                order. The line moves at the event's `admitPerMinute`. Once admitted, you have a few minutes to choose
                places; after that, joining again puts you at the back. Events without a waiting room answer `admitted: true`.
                """);
        group.MapGet("/", Status).RequireRateLimiting(RateLimit).WithSummary("Where you are in line");
        return app;
    }

    private static async Task<Ok<QueueStatus>> Join(Guid eventId, ClaimsPrincipal user, GiseDbContext db, QueueService queue, CancellationToken ct)
    {
        var @event = await CatalogEndpoints.FindPublicAsync(db, eventId, ct);
        return TypedResults.Ok(await queue.JoinAsync(@event, user.UserId(), ct));
    }

    private static async Task<Ok<QueueStatus>> Status(Guid eventId, ClaimsPrincipal user, GiseDbContext db, QueueService queue, CancellationToken ct)
    {
        var @event = await CatalogEndpoints.FindPublicAsync(db, eventId, ct);
        return TypedResults.Ok(await queue.StatusAsync(@event, user.UserId(), ct));
    }
}
