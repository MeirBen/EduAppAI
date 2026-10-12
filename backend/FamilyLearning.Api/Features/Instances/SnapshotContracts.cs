using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

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

/// <summary>Frozen parent-only preview, including keys and release evidence. Never use this DTO for child access.</summary>
public sealed record SnapshotPreview(Guid Id, Guid SourceDraftId, long SourceDraftRevision, LearningPlan Plan, TaskDocument Document, LengthMeasurement[] Measurements, int EngineRevision,
    Guid? SourceSnapshotId, string CreatedByParentId, DateTime DraftCreatedAtUtc,
    string ReviewedByParentId, DateTime ReviewedAtUtc, DateTime? ArchivedAtUtc)
{
    internal static SnapshotPreview From(TaskSnapshot snapshot) => new(snapshot.Id, snapshot.SourceDraftId, snapshot.SourceDraftRevision,
        StoredJson.Read<LearningPlan>(snapshot.PlanJson), StoredJson.Read<TaskDocument>(snapshot.DocumentJson),
        StoredJson.Read<LengthMeasurement[]>(snapshot.MeasurementsJson), snapshot.EngineRevision,
        snapshot.SourceSnapshotId, snapshot.CreatedByParentId, snapshot.DraftCreatedAtUtc,
        snapshot.ReviewedByParentId, snapshot.ReviewedAtUtc, snapshot.ArchivedAtUtc);
}
