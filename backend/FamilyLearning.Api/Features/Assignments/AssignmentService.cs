using System.Linq.Expressions;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Owns family assignment queries and serializes creation, withdrawal and restore.</summary>
internal sealed class AssignmentService(LearningDbContext db, TimeProvider clock)
{
    private static readonly Expression<Func<Assignment, AssignmentSummary>> SummaryProjection = a =>
        new(a.Id, a.ChildId, a.Child.Name, a.SnapshotId, a.Snapshot.Title, a.Status, a.Revision, a.CreatedAtUtc, a.Session != null,
            a.Status == "completed" ? LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.finalTotal") : null,
            a.Status == "completed" ? LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.possibleTotal") : null);

    internal async Task<FeatureResult<AssignmentDetail>> ReadAsync(Guid id, Guid familyId, CancellationToken ct)
    {
        var query = db.Assignments.AsNoTracking().Where(a => a.Id == id && a.FamilyId == familyId);
        var summary = await query.Select(SummaryProjection).SingleOrDefaultAsync(ct);
        if (summary is null) return FeatureFailure.NotFound;
        var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == summary.SnapshotId && s.FamilyId == familyId, ct);
        if (snapshot is null) return FeatureFailure.NotFound;
        var timing = await db.TaskSessions.AsNoTracking().Where(s => s.AssignmentId == id)
            .Select(s => new { s.StartedAtUtc, s.SavedAtUtc, s.SubmittedAtUtc }).SingleOrDefaultAsync(ct);
        return new AssignmentDetail(summary, await SnapshotReader.ReadAsync(db, snapshot, ct), timing?.StartedAtUtc, timing?.SavedAtUtc, timing?.SubmittedAtUtc);
    }

    internal async Task<FeatureResult<(AssignmentSummary Value, bool Created)>> CreateAsync(
        CreateAssignmentRequest request, Guid familyId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Replay precedes eligibility: disabling a profile or archiving content cannot reopen or erase existing work.
        var existing = await db.Assignments.AsNoTracking().Where(a => a.FamilyId == familyId &&
            a.ChildId == request.ChildId && a.SnapshotId == request.SnapshotId).Select(SummaryProjection).SingleOrDefaultAsync(ct);
        if (existing is not null) return (existing, false);
        var child = await db.Children.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.ChildId && c.FamilyId == familyId, ct);
        var snapshot = await db.TaskSnapshots.AsNoTracking().Where(s => s.Id == request.SnapshotId && s.FamilyId == familyId)
            .Select(s => new { s.ArchivedAtUtc }).SingleOrDefaultAsync(ct);
        if (child is null || snapshot is null) return FeatureFailure.NotFound;
        if (!child.Enabled || snapshot.ArchivedAtUtc is not null) return Ineligible();
        var assignment = new Assignment(familyId, child.Id, request.SnapshotId, clock.GetUtcNow().UtcDateTime);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        var summary = await db.Assignments.Where(a => a.Id == assignment.Id).Select(SummaryProjection).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return (summary, true);
    }

    internal async Task<FeatureResult<PageResponse<AssignmentSummary>>> ListAsync(Guid familyId, CancellationToken ct,
        Guid? childId = null, Guid? snapshotId = null, string? status = null, int page = 1,
        int pageSize = PageRequest.DefaultSize)
    {
        var paging = new PageRequest(page, pageSize);
        if (!paging.IsValid) return PageRequest.Invalid();
        if (status is not (null or "open" or "assigned" or "withdrawn" or "awaiting-review" or "completed"))
            return FeatureFailure.Invalid("status", "מצב ההקצאה אינו תקף.");
        var query = db.Assignments.AsNoTracking().Where(a => a.FamilyId == familyId);
        if (childId.HasValue) query = query.Where(a => a.ChildId == childId.Value);
        if (snapshotId.HasValue) query = query.Where(a => a.SnapshotId == snapshotId.Value);
        // Open work still waits for the child or for the parent's grading. Withdrawn work is history: only its own filter lists it.
        query = status switch
        {
            null => query.Where(a => a.Status != "withdrawn"),
            "open" => query.Where(a => a.Status == "assigned" || a.Status == "awaiting-review"),
            _ => query.Where(a => a.Status == status),
        };
        return await paging.ReadAsync(query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(SummaryProjection), ct);
    }

    /// <summary>Exact sizes of the list's views, for one child or the whole family, read in one grouped query.</summary>
    internal async Task<AssignmentCounts> CountsAsync(Guid familyId, CancellationToken ct,
        Guid? childId = null)
    {
        var query = db.Assignments.AsNoTracking().Where(a => a.FamilyId == familyId);
        if (childId.HasValue) query = query.Where(a => a.ChildId == childId.Value);
        var counts = await query.GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);
        int Count(string status) => counts.GetValueOrDefault(status);
        return new AssignmentCounts(Count("assigned") + Count("awaiting-review"), Count("completed"), Count("withdrawn"));
    }

    internal async Task<FeatureResult<AssignmentSummary>> WithdrawAsync(Guid id, AssignmentStateRequest request,
        Guid familyId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id && a.FamilyId == familyId, ct);
        if (assignment is null) return FeatureFailure.NotFound;
        if (request.ExpectedRevision <= 0) return InvalidRevision();
        if (assignment.Status != "withdrawn")
        {
            if (assignment.Status != "assigned" || assignment.Revision != request.ExpectedRevision) return Changed();
            assignment.Withdraw(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        var summary = await db.Assignments.Where(a => a.Id == id).Select(SummaryProjection).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return summary;
    }

    /// <summary>An explicit undo of withdrawal; creation never restores, so a delayed duplicate cannot undo a later withdrawal.</summary>
    internal async Task<FeatureResult<AssignmentSummary>> RestoreAsync(Guid id, AssignmentStateRequest request,
        Guid familyId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id && a.FamilyId == familyId, ct);
        if (assignment is null) return FeatureFailure.NotFound;
        if (request.ExpectedRevision <= 0) return InvalidRevision();
        if (assignment.Status != "assigned")
        {
            if (assignment.Status != "withdrawn" || assignment.Revision != request.ExpectedRevision) return Changed();
            // Restoring reopens access, so it needs the same eligibility as a new assignment.
            if (!await db.Children.AnyAsync(c => c.Id == assignment.ChildId && c.Enabled, ct) ||
                !await db.TaskSnapshots.AnyAsync(s => s.Id == assignment.SnapshotId && s.ArchivedAtUtc == null, ct))
                return Ineligible();
            assignment.Restore();
            await db.SaveChangesAsync(ct);
        }
        var summary = await db.Assignments.Where(a => a.Id == id).Select(SummaryProjection).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return summary;
    }

    private static FeatureFailure InvalidRevision() => FeatureFailure.Invalid("expectedRevision", "יש לציין גרסה חיובית.");
    private static FeatureFailure Changed() => FeatureFailure.Conflict("ההקצאה השתנתה או שכבר נשלחה. יש לטעון אותה מחדש.");
    private static FeatureFailure Ineligible() => FeatureFailure.Conflict("אפשר להקצות פעילות רק לילד פעיל ומתוך הספרייה הפעילה.");
}
