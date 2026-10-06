using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Server-owned identity validated against the current profile and device grant.</summary>
public sealed record ChildIdentity(Guid ChildId, Guid FamilyId, Guid DeviceGrantId);

public static class ChildAccess
{
    /// <summary>Reads current database values, returning null for disabled, expired, revoked or missing access.</summary>
    /// <remarks>Writes must call this again inside their transaction with UTC sampled after acquiring it.
    /// AsNoTracking deliberately avoids authentication-time entities in the scoped context.</remarks>
    public static Task<ChildIdentity?> FindAsync(ClaimsPrincipal principal, LearningDbContext db, DateTime utcNow, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true || !principal.IsInRole("Child") ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var childId) ||
            !Guid.TryParse(principal.FindFirstValue("device_grant_id"), out var grantId) ||
            !Guid.TryParse(principal.FindFirstValue("family_id"), out var familyId))
            return Task.FromResult<ChildIdentity?>(null);
        return (from child in db.Children.AsNoTracking()
                join grant in db.ChildDeviceGrants.AsNoTracking() on child.Id equals grant.ChildId
                where child.Id == childId && child.FamilyId == familyId && child.Enabled && grant.Id == grantId &&
                    grant.RevokedAtUtc == null && grant.ExpiresAtUtc > utcNow
                select new ChildIdentity(child.Id, child.FamilyId, grant.Id)).SingleOrDefaultAsync(ct);
    }
}
