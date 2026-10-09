using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Ready snapshot list entry without content or answer keys.</summary>
public sealed record SnapshotSummary(Guid Id, string Title, string Status, DateTime CreatedAtUtc, bool HasAssignments);

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
