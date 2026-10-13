using System.Security.Claims;
using FamilyLearning.Api.Features.Library;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Validates child access and commits activation or revocation before publishing family change notes.</summary>
internal sealed class ChildAuthService(LearningDbContext db, ChildAccess access, LibraryChanges changes, TimeProvider clock)
{
    internal async Task<FeatureResult<ChildSessionIdentity>> MeAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var identity = await access.FindAsync(user, clock.GetUtcNow().UtcDateTime, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var result = await (from child in db.Children.AsNoTracking()
                            join grant in db.ChildDeviceGrants.AsNoTracking() on child.Id equals grant.ChildId
                            where child.Id == identity.ChildId && grant.Id == identity.DeviceGrantId
                            select new ChildSessionIdentity(child.Id, child.Name, grant.ExpiresAtUtc, EngineValidation.AnswerLength)).SingleOrDefaultAsync(ct);
        if (result is null) return FeatureFailure.Unauthorized;
        return result;
    }

    internal async Task<FeatureResult<ChildActivationSession>> ActivateAsync(ActivateChildRequest request, CancellationToken ct)
    {
        if (ActivationCode.Normalize(request.Code) is not { } code) return InvalidCode();
        var hash = ActivationCode.Hash(code);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var activation = await db.ChildActivations.SingleOrDefaultAsync(a => a.CodeHash == hash &&
            a.ConsumedAtUtc == null && a.ExpiresAtUtc > now, ct);
        if (activation is null) return InvalidCode();
        var child = await db.Children.AsNoTracking().SingleOrDefaultAsync(c => c.Id == activation.ChildId && c.Enabled, ct);
        if (child is null) return InvalidCode();
        activation.Consume(now);
        var grant = new ChildDeviceGrant(child.Id, activation.DeviceLabel, now, now.AddDays(30));
        db.ChildDeviceGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        changes.Publish(child.FamilyId);
        return new ChildActivationSession(new(child.Id, child.FamilyId, grant.Id), now, grant.ExpiresAtUtc);
    }

    internal async Task<FeatureResult<bool>> LogoutAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var identity = await access.FindAsync(user, now, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var grant = await db.ChildDeviceGrants.SingleAsync(g => g.Id == identity.DeviceGrantId, ct);
        grant.Revoke(now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        changes.Publish(identity.FamilyId);
        return true;
    }

    private static FeatureFailure InvalidCode() => FeatureFailure.Invalid("קוד ההפעלה אינו תקף. יש לבקש קוד חדש מההורה.");
}

/// <summary>Committed device access and fixed UTC lifetime used by the endpoint to issue its cookie.</summary>
internal sealed record ChildActivationSession(ChildIdentity Identity, DateTime IssuedAtUtc, DateTime ExpiresAtUtc);
