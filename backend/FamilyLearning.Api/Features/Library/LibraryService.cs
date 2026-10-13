using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Library;

/// <summary>Owns family library counts and explicit data deletions, retaining parent accounts and AI configuration.</summary>
internal sealed class LibraryService(LearningDbContext db)
{
    /// <summary>Reads exact section sizes in one query using the same views as the snapshot list.</summary>
    internal Task<LibraryCounts> CountsAsync(Guid familyId, CancellationToken ct)
    {
        var ready = db.TaskSnapshots.Where(s => s.FamilyId == familyId && s.ArchivedAtUtc == null);
        var (assigned, unassigned, completed) =
            (ready.InView(db, familyId, "assigned"), ready.InView(db, familyId, "unassigned"), ready.InView(db, familyId, "completed"));
        return db.Families.AsNoTracking().Where(f => f.Id == familyId).Select(f => new LibraryCounts(
            db.ActivityDrafts.Count(d => d.FamilyId == f.Id && d.ReleasedSnapshotId == null),
            assigned.Count(), unassigned.Count(), completed.Count(),
            db.TaskSnapshots.Count(s => s.FamilyId == f.Id && s.ArchivedAtUtc != null))).SingleAsync(ct);
    }

    /// <summary>Deletes the family's assigned work, drafts and snapshots in one transaction.</summary>
    internal Task DeleteActivitiesAsync(Guid familyId, CancellationToken ct) =>
        DeleteAsync(familyId, ct, async id =>
        {
            await db.ActivityDrafts.Where(d => d.FamilyId == id).ExecuteDeleteAsync(ct);
            await db.TaskSnapshots.Where(s => s.FamilyId == id).ExecuteDeleteAsync(ct);
        });

    /// <summary>Deletes the family's assigned work, child profiles, device access and retained archive in one transaction.</summary>
    internal Task DeleteChildrenAsync(Guid familyId, CancellationToken ct) =>
        DeleteAsync(familyId, ct, async id =>
        {
            var children = db.Children.Where(c => c.FamilyId == id).Select(c => c.Id);
            await db.ChildDeviceGrants.Where(g => children.Contains(g.ChildId)).ExecuteDeleteAsync(ct);
            await db.ChildActivations.Where(a => children.Contains(a.ChildId)).ExecuteDeleteAsync(ct);
            await db.Children.Where(c => c.FamilyId == id).ExecuteDeleteAsync(ct);
            // The archive keeps removed activities only for the work deleted here.
            await db.TaskSnapshots.Where(s => s.FamilyId == id && s.ArchivedAtUtc != null).ExecuteDeleteAsync(ct);
        });

    /// <summary>Assigned work belongs to both an activity and a child, so either deletion removes it first.</summary>
    private async Task DeleteAsync(Guid familyId, CancellationToken ct, Func<Guid, Task> deleteRest)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.TaskSessions.Where(s => s.Assignment.FamilyId == familyId).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.FamilyId == familyId).ExecuteDeleteAsync(ct);
        await deleteRest(familyId);
        await transaction.CommitAsync(ct);
    }
}
