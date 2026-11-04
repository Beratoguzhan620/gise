using System.Security.Claims;
using Gise.Api.Common;
using Gise.Core.Events;
using Gise.Core.Organizations;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Organizations;

public sealed record CreateOrganizationRequest(string Name);

public sealed record OrganizationResponse(Guid Id, string Name, Role Role, int Members, int Venues, int Events, DateTimeOffset CreatedAt);

internal static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations").WithTags("Organizers").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The organizers you belong to");
        group.MapPost("/", Create).WithSummary("Start selling: create an organizer")
            .WithDescription("You become its owner and can add managers and gate staff.");
        group.MapGet("/{organizationId:guid}", Get).WithSummary("One of your organizers");
        group.MapPatch("/{organizationId:guid}", Rename).WithSummary("Rename (owners)");
        return app;
    }

    internal static async Task<List<OrganizationResponse>> ListForAsync(GiseDbContext db, Guid userId, CancellationToken ct) =>
        await Query(db, userId, null).ToListAsync(ct);

    private static async Task<Ok<List<OrganizationResponse>>> List(ClaimsPrincipal user, GiseDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListForAsync(db, user.UserId(), ct));

    private static async Task<Results<Created<OrganizationResponse>, ValidationProblem>> Create(
        CreateOrganizationRequest request, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation().Require(
            request.Name?.Trim().Length is >= Organization.NameMinLength and <= Organization.NameMaxLength,
            "name", $"A name has {Organization.NameMinLength} to {Organization.NameMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (organization, owner) = Organization.Create(request.Name!, user.UserId(), clock.GetUtcNow());
        db.Organizations.Add(organization);
        db.Memberships.Add(owner);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/organizations/{organization.Id}",
            new OrganizationResponse(organization.Id, organization.Name, Role.Owner, 1, 0, 0, organization.CreatedAt));
    }

    private static async Task<Ok<OrganizationResponse>> Get(Guid organizationId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        return TypedResults.Ok(await Query(db, user.UserId(), organizationId).SingleAsync(ct));
    }

    private static async Task<Ok<OrganizationResponse>> Rename(
        Guid organizationId, CreateOrganizationRequest request, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Owner, ct);
        var organization = await db.Organizations.SingleAsync(o => o.Id == organizationId, ct);
        organization.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await Query(db, user.UserId(), organizationId).SingleAsync(ct));
    }

    private static IQueryable<OrganizationResponse> Query(GiseDbContext db, Guid userId, Guid? organizationId) =>
        from m in db.Memberships
        join o in db.Organizations on m.OrganizationId equals o.Id
        where m.UserId == userId && (organizationId == null || o.Id == organizationId)
        orderby o.Name
        select new OrganizationResponse(
            o.Id,
            o.Name,
            m.Role,
            db.Memberships.Count(x => x.OrganizationId == o.Id),
            db.Venues.Count(v => v.OrganizationId == o.Id),
            db.Events.Count(e => e.OrganizationId == o.Id && e.Status != EventStatus.Cancelled),
            o.CreatedAt);
}
