using System.Security.Claims;
using Gise.Api.Common;
using Gise.Api.Features.Auth;
using Gise.Core;
using Gise.Core.Users;
using Gise.Core.Organizations;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Organizations;

public sealed record AddMemberRequest(string Email, Role Role);

public sealed record ChangeRoleRequest(Role Role);

public sealed record MemberResponse(Guid UserId, string DisplayName, string Email, Role Role, DateTimeOffset JoinedAt);

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/members").WithTags("Team").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The team");
        group.MapPost("/", Add).WithSummary("Add someone who has an account (owners)")
            .WithDescription("`role` is `Staff` (gate), `Manager` or `Owner`.");
        group.MapPatch("/{userId:guid}", ChangeRole).WithSummary("Change a member's role (owners)");
        group.MapDelete("/{userId:guid}", Remove).WithSummary("Remove a member (owners), or leave (yourself)")
            .WithDescription("An organizer always keeps at least one owner.");
        return app;
    }

    private static async Task<Ok<List<MemberResponse>>> List(Guid organizationId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Staff, ct);
        var members = await Members(db, organizationId, null).ToListAsync(ct);
        return TypedResults.Ok(members.OrderByDescending(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture).ToList());
    }

    private static async Task<Results<Created<MemberResponse>, ValidationProblem>> Add(
        Guid organizationId, AddMemberRequest request, ClaimsPrincipal user, GiseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Owner, ct);
        var validation = new Validation()
            .Require(AuthEndpoints.IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(Enum.IsDefined(request.Role), "role", "A role is Staff, Manager or Owner.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var invitee = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Nobody has an account with this email yet. Ask them to sign up, then add them.");
        if (await db.Memberships.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == invitee.Id, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already on the team.");
        }

        var membership = Membership.Create(organizationId, invitee.Id, request.Role, clock.GetUtcNow());
        db.Memberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already on the team.");
        }

        return TypedResults.Created($"/api/organizations/{organizationId}/members/{invitee.Id}",
            new MemberResponse(invitee.Id, invitee.DisplayName, invitee.Email, membership.Role, membership.JoinedAt));
    }

    private static async Task<Ok<MemberResponse>> ChangeRole(
        Guid organizationId, Guid userId, ChangeRoleRequest request, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        await OrganizationAccess.RequireAsync(db, organizationId, user, Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await OrganizationAccess.LockAsync(db, organizationId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");

        var owners = await db.Memberships.CountAsync(m => m.OrganizationId == organizationId && m.Role == Role.Owner, ct);
        var wasOwner = membership.Role == Role.Owner;
        membership.ChangeRole(request.Role);
        Organization.EnsureAnOwnerRemains(owners - (wasOwner ? 1 : 0) + (membership.Role == Role.Owner ? 1 : 0));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await Members(db, organizationId, userId).SingleAsync(ct));
    }

    private static async Task<NoContent> Remove(
        Guid organizationId, Guid userId, ClaimsPrincipal user, GiseDbContext db, CancellationToken ct)
    {
        // Anyone may leave; removing someone else takes an owner.
        await OrganizationAccess.RequireAsync(db, organizationId, user, userId == user.UserId() ? Role.Staff : Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await OrganizationAccess.LockAsync(db, organizationId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");
        if (membership.Role == Role.Owner)
        {
            var owners = await db.Memberships.CountAsync(m => m.OrganizationId == organizationId && m.Role == Role.Owner, ct);
            Organization.EnsureAnOwnerRemains(owners - 1);
        }

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static IQueryable<MemberResponse> Members(GiseDbContext db, Guid organizationId, Guid? userId) =>
        from m in db.Memberships
        join u in db.Users on m.UserId equals u.Id
        where m.OrganizationId == organizationId && (userId == null || m.UserId == userId)
        select new MemberResponse(u.Id, u.DisplayName, u.Email, m.Role, m.JoinedAt);
}
