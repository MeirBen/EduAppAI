using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Owns family-scoped profiles and devices; state-dependent writes serialize in SQLite.</summary>
internal sealed class ChildService(LearningDbContext db, TimeProvider clock)
{
    internal async Task<FeatureResult<ChildSummary>> CreateAsync(Guid familyId, CreateChildRequest request, CancellationToken ct)
    {
        if (!ChildValidation.ValidName(request.Name)) return ChildValidation.InvalidName("name");
        if (ChildValidation.ValidateDetails(request.Details) is { } invalid) return invalid;
        var now = clock.GetUtcNow().UtcDateTime;
        var child = new Child(familyId, request.Name.Trim(), now);
        if (request.Details is { } details) child.SetDetails(details, now);
        db.Children.Add(child);
        await db.SaveChangesAsync(ct);
        return ChildSummary.From(child, hasAssignments: false);
    }

    internal async Task<FeatureResult<PageResponse<ChildSummary>>> ListAsync(Guid familyId, PageRequest paging, CancellationToken ct)
    {
        if (!paging.IsValid) return PageRequest.Invalid();
        return await paging.ReadAsync(db.Children.AsNoTracking().Where(c => c.FamilyId == familyId)
            .OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            .Select(c => new ChildSummary(c.Id, c.Name, c.Enabled, c.Revision, c.CreatedAtUtc, c.Grade, c.Age, c.AgeConfirmedAtUtc, c.UpdatedAtUtc,
                db.Assignments.Any(a => a.ChildId == c.Id))), ct);
    }

    internal async Task<FeatureResult<ChildSummary>> UpdateAsync(Guid familyId, Guid id, UpdateChildRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == familyId, ct);
        if (child is null) return FeatureFailure.NotFound;
        if (!ChildValidation.ValidName(request.Name)) return ChildValidation.InvalidName("name");
        if (ChildValidation.ValidateDetails(request.Details) is { } invalid) return invalid;
        if (request.ExpectedRevision != child.Revision) return FeatureFailure.Conflict("פרטי הילד השתנו. יש לטעון אותם מחדש.");
        var now = clock.GetUtcNow().UtcDateTime;
        child.Update(request.Name.Trim(), request.Enabled, now);
        if (request.Details is { } details) child.SetDetails(details, now);
        if (!child.Enabled)
        {
            await db.ChildActivations.Where(a => a.ChildId == id).ExecuteDeleteAsync(ct);
            await db.ChildDeviceGrants.Where(g => g.ChildId == id && g.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAtUtc, now), ct);
        }
        await db.SaveChangesAsync(ct);
        var hasAssignments = await db.Assignments.AnyAsync(a => a.ChildId == id, ct);
        await transaction.CommitAsync(ct);
        return ChildSummary.From(child, hasAssignments);
    }

    internal async Task<FeatureResult<bool>> DeleteAsync(Guid familyId, Guid id, long expectedRevision, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var child = await db.Children.SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == familyId, ct);
        if (child is null) return FeatureFailure.NotFound;
        if (expectedRevision != child.Revision) return FeatureFailure.Conflict("פרטי הילד השתנו. יש לטעון אותם מחדש.");
        if (await db.Assignments.AnyAsync(a => a.ChildId == id, ct))
            return FeatureFailure.Conflict("לפרופיל יש פעילויות שמורות. אפשר להשבית אותו כדי לשמור על ההיסטוריה.");
        await db.ChildActivations.Where(a => a.ChildId == id).ExecuteDeleteAsync(ct);
        await db.ChildDeviceGrants.Where(g => g.ChildId == id).ExecuteDeleteAsync(ct);
        db.Children.Remove(child);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    internal async Task<FeatureResult<ChildActivationCode>> IssueAsync(Guid familyId, Guid id, CreateActivationRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var child = await db.Children.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.FamilyId == familyId, ct);
        if (child is null) return FeatureFailure.NotFound;
        if (!ChildValidation.ValidName(request.DeviceLabel)) return ChildValidation.InvalidName("deviceLabel");
        if (!child.Enabled) return FeatureFailure.Conflict("יש להפעיל את פרופיל הילד לפני יצירת קוד.");
        var code = ActivationCode.New();
        var hash = ActivationCode.Hash(code.Canonical);
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(10);
        var activation = await db.ChildActivations.SingleOrDefaultAsync(a => a.ChildId == id, ct);
        if (activation is null) db.ChildActivations.Add(new ChildActivation(id, hash, request.DeviceLabel.Trim(), expires));
        else activation.Replace(hash, request.DeviceLabel.Trim(), expires);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ChildActivationCode(code.Display, expires);
    }

    internal async Task<FeatureResult<PageResponse<ChildDeviceSummary>>> DevicesAsync(Guid familyId, Guid id, PageRequest paging, CancellationToken ct)
    {
        if (!paging.IsValid) return PageRequest.Invalid();
        if (!await db.Children.AnyAsync(c => c.Id == id && c.FamilyId == familyId, ct)) return FeatureFailure.NotFound;
        var now = clock.GetUtcNow().UtcDateTime;
        return await paging.ReadAsync(db.ChildDeviceGrants.AsNoTracking().Where(g => g.ChildId == id)
            .OrderByDescending(g => g.CreatedAtUtc).ThenByDescending(g => g.Id)
            .Select(g => new ChildDeviceSummary(g.Id, g.DeviceLabel, g.CreatedAtUtc, g.ExpiresAtUtc, g.RevokedAtUtc,
                g.RevokedAtUtc != null || g.ExpiresAtUtc <= now)), ct);
    }

    internal async Task<FeatureResult<bool>> RevokeAsync(Guid familyId, Guid id, Guid grantId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var grant = await OwnedGrant(familyId, id, grantId).SingleOrDefaultAsync(ct);
        if (grant is null) return FeatureFailure.NotFound;
        grant.Revoke(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    internal async Task<FeatureResult<bool>> RemoveDeviceAsync(Guid familyId, Guid id, Guid grantId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var grant = await OwnedGrant(familyId, id, grantId).SingleOrDefaultAsync(ct);
        if (grant is null) return FeatureFailure.NotFound;
        if (grant.RevokedAtUtc is null && grant.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime)
            return FeatureFailure.Conflict("יש לבטל את הגישה מהמכשיר לפני הסרתו מהרשימה.");
        db.ChildDeviceGrants.Remove(grant);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private IQueryable<ChildDeviceGrant> OwnedGrant(Guid familyId, Guid childId, Guid grantId) =>
        from device in db.ChildDeviceGrants
        join child in db.Children on device.ChildId equals child.Id
        where child.Id == childId && child.FamilyId == familyId && device.Id == grantId
        select device;
}
