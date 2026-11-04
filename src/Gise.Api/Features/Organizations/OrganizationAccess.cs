using System.Security.Claims;
using Gise.Api.Common;
using Gise.Core;
using Gise.Core.Organizations;
using Gise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gise.Api.Features.Organizations;

/// <summary>
/// The one gate in front of everything an organizer owns. Membership and role are read from the database on every
/// request, so a member who is removed or demoted loses access on their next click.
/// </summary>
internal static class OrganizationAccess
{
    public static async Task<Membership> RequireAsync(GiseDbContext db, Guid organizationId, ClaimsPrincipal user, Role needed, CancellationToken ct)
    {
        var userId = user.UserId();
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct)
            // Not "forbidden": whether an organizer with this id exists is none of an outsider's business.
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no organizer with this id among yours.");

        if (!membership.Can(needed))
        {
            throw new DomainException(ErrorCodes.Forbidden, needed switch
            {
                Role.Owner => "Only the organizer's owners can do this.",
                _ => "Only managers and owners can do this; staff check tickets at the gate.",
            });
        }

        return membership;
    }

    /// <summary>
    /// Locks the organizer's row for the rest of the transaction, so two changes to the team (two owners
    /// demoting each other, say) are checked one after the other and cannot leave it without an owner.
    /// </summary>
    public static Task LockAsync(GiseDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM organizations WHERE id = {organizationId} FOR UPDATE", ct);
}
