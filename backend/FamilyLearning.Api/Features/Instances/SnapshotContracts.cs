using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>
/// Library or archive list entry without content or answer keys. <c>CreatedAtUtc</c> is the review time,
/// <c>AssignedAtUtc</c> the latest open assignment, <c>CompletedAtUtc</c> the latest completion, and <c>Children</c> lists
/// open and completed work by child name.
/// </summary>
public sealed record SnapshotSummary(Guid Id, string Title, DateTime CreatedAtUtc, DateTime? AssignedAtUtc,
    DateTime? CompletedAtUtc, DateTime? ArchivedAtUtc, SnapshotChild[] Children);
/// <summary>One child's work on a snapshot; completed work carries its final score from the frozen evaluation.</summary>
public sealed record SnapshotChild(string ChildName, string Status, int? FinalTotal, int? PossibleTotal);

/// <summary>
/// Frozen parent-only preview, including keys and release evidence. <c>FirstMessage</c> is the parent's first chat message
/// in the draft the activity began from, for starting another activity with it. Never use this DTO for child access.
/// </summary>
public sealed record SnapshotPreview(Guid Id, Guid SourceDraftId, long SourceDraftRevision, LearningPlan Plan, TaskDocument Document, LengthMeasurement[] Measurements, int EngineRevision,
    Guid? SourceSnapshotId, string CreatedByParentId, DateTime DraftCreatedAtUtc,
    string ReviewedByParentId, DateTime ReviewedAtUtc, DateTime? ArchivedAtUtc, string? FirstMessage)
{
    internal static async Task<SnapshotPreview> ReadAsync(LearningDbContext db, TaskSnapshot snapshot, CancellationToken ct) => new(
        snapshot.Id, snapshot.SourceDraftId, snapshot.SourceDraftRevision,
        StoredJson.Read<LearningPlan>(snapshot.PlanJson), StoredJson.Read<TaskDocument>(snapshot.DocumentJson),
        StoredJson.Read<LengthMeasurement[]>(snapshot.MeasurementsJson), snapshot.EngineRevision,
        snapshot.SourceSnapshotId, snapshot.CreatedByParentId, snapshot.DraftCreatedAtUtc,
        snapshot.ReviewedByParentId, snapshot.ReviewedAtUtc, snapshot.ArchivedAtUtc,
        await FirstMessageAsync(db, snapshot.FamilyId, snapshot.SourceDraftId, ct));

    /// <summary>
    /// Follows copies back through the drafts that released their sources, which keep that link after a snapshot's deletion;
    /// null once a draft on the way is gone. A chat past its turn limit keeps only its newest turns.
    /// </summary>
    internal static async Task<string?> FirstMessageAsync(LearningDbContext db, Guid familyId, Guid sourceDraftId, CancellationToken ct)
    {
        var drafts = db.ActivityDrafts.Where(d => d.FamilyId == familyId)
            .Select(d => new { d.Id, d.ReleasedSnapshotId, d.SourceSnapshotId, d.ChatJson });
        var draft = await drafts.SingleOrDefaultAsync(d => d.Id == sourceDraftId, ct);
        while (draft?.SourceSnapshotId is { } source)
            draft = await drafts.SingleOrDefaultAsync(d => d.ReleasedSnapshotId == source, ct);
        return draft is null ? null : StoredJson.Read<ActivityChatTurn[]>(draft.ChatJson).FirstOrDefault(t => t.Role == "parent")?.Text;
    }
}
