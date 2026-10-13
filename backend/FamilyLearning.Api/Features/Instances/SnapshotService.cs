using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>
/// Owns family-scoped parent snapshot reads and removal. Assigned content is archived unchanged; only explicit archive
/// deletion erases it together with its assignments and their work.
/// </summary>
internal sealed class SnapshotService(LearningDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Lists one page, in one view when <paramref name="status"/> names it, newest first by the date each row shows: when
    /// it was archived, else its latest open assignment, else its latest completion, else its review.
    /// </summary>
    internal async Task<FeatureResult<PageResponse<SnapshotSummary>>> ListAsync(Guid familyId, bool archived, string? status,
        PageRequest paging, CancellationToken ct)
    {
        if (!paging.IsValid) return PageRequest.Invalid();
        if (status is not (null or "assigned" or "unassigned" or "completed"))
            return FeatureFailure.Invalid("status", "מצב הפעילות אינו תקף.");
        var snapshots = db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == familyId);
        snapshots = archived ? snapshots.Where(s => s.ArchivedAtUtc != null) : snapshots.Where(s => s.ArchivedAtUtc == null);
        if (status is not null) snapshots = snapshots.InView(db, familyId, status);
        var (open, finished) = SnapshotQueries.Work(db, familyId);
        var rows = snapshots.Select(s => new
        {
            Snapshot = s,
            AssignedAtUtc = open.Where(a => a.SnapshotId == s.Id).Max(a => (DateTime?)a.CreatedAtUtc),
            CompletedAtUtc = finished.Where(a => a.SnapshotId == s.Id).Max(a => a.Session!.ReviewedAtUtc ?? a.Session!.SubmittedAtUtc),
        });
        return await paging.ReadAsync(rows
            .OrderByDescending(r => r.Snapshot.ArchivedAtUtc ?? r.AssignedAtUtc ?? r.CompletedAtUtc ?? r.Snapshot.ReviewedAtUtc)
            .ThenByDescending(r => r.Snapshot.Id)
            .Select(r => new SnapshotSummary(r.Snapshot.Id, r.Snapshot.Title, r.Snapshot.ReviewedAtUtc, r.AssignedAtUtc,
                r.CompletedAtUtc, r.Snapshot.ArchivedAtUtc,
                db.Assignments.Where(a => a.FamilyId == familyId && a.SnapshotId == r.Snapshot.Id && a.Status != "withdrawn")
                    .OrderBy(a => a.Child.Name).ThenBy(a => a.Id)
                    .Select(a => new SnapshotChild(a.Child.Name, a.Status,
                        LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.finalTotal"),
                        LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.possibleTotal")))
                    .ToArray())), ct);
    }

    internal Task<List<string>> FirstMessagesAsync(Guid familyId, CancellationToken ct) =>
        SnapshotReader.FirstMessagesAsync(db, familyId, ct);

    /// <summary>Verifies ownership before reading the parent-only preview, including answer keys.</summary>
    internal async Task<FeatureResult<SnapshotPreview>> ReadAsync(Guid familyId, Guid id, CancellationToken ct)
    {
        var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.FamilyId == familyId, ct);
        if (snapshot is null) return FeatureFailure.NotFound;
        return await SnapshotReader.ReadAsync(db, snapshot, ct);
    }

    /// <summary>Serializes assignment checks with removal so existing work retains its immutable snapshot.</summary>
    internal async Task<FeatureResult<bool>> RemoveAsync(Guid familyId, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var snapshots = db.TaskSnapshots.Where(s => s.Id == id && s.FamilyId == familyId);
        if (!await snapshots.AnyAsync(ct)) return FeatureFailure.NotFound;
        if (await db.Assignments.AnyAsync(a => a.FamilyId == familyId && a.SnapshotId == id, ct))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await snapshots.Where(s => s.ArchivedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ArchivedAtUtc, now), ct);
        }
        else await snapshots.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Permanently deletes an archived snapshot with its assignments and sessions in one transaction. Content still in
    /// the library is not found, so removing it twice can never erase children's work.
    /// </summary>
    internal async Task<FeatureResult<bool>> DeleteArchivedAsync(Guid familyId, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var snapshot = db.TaskSnapshots.Where(s => s.Id == id && s.FamilyId == familyId && s.ArchivedAtUtc != null);
        if (!await snapshot.AnyAsync(ct)) return FeatureFailure.NotFound;
        await db.TaskSessions.Where(s => s.Assignment.FamilyId == familyId && s.Assignment.SnapshotId == id).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.FamilyId == familyId && a.SnapshotId == id).ExecuteDeleteAsync(ct);
        await snapshot.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
