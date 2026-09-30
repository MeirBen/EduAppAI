using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>List projection without questions, parameters or answer keys.</summary>
public sealed record InstanceSummary(Guid Id, string Title, string Status, DateTime CreatedAtUtc);

/// <summary>A parent-only view of a saved draft, including its answer keys.</summary>
/// <remarks>Child endpoints must project a separate answer-free DTO after checking assignment access.</remarks>
public sealed record InstancePreview(Guid Id, Guid TemplateVersionId, int TemplateVersion,
    TaskInput Input, TaskContent Content, string Status, DateTime CreatedAtUtc,
    GenerationMetadata? GenerationMetadata)
{
    /// <summary>Reads an authorized task snapshot without calling AI.</summary>
    /// <param name="instance">The instance belonging to the authenticated parent's family.</param>
    /// <param name="version">Revision number of the instance's referenced template version.</param>
    public static InstancePreview From(TaskInstance instance, int version) => new(instance.Id,
        instance.TemplateVersionId, version, StoredJson.Read<TaskInput>(instance.InputJson),
        StoredJson.Read<TaskContent>(instance.ContentJson), instance.Status, instance.CreatedAtUtc,
        instance.GenerationMetadataJson is { } json ? StoredJson.Read<GenerationMetadata>(json) : null);
}

/// <summary>Frozen parent-only preview, including keys and release evidence. Never use this DTO for child access.</summary>
public sealed record SnapshotPreview(Guid Id, Guid SourceDraftId, long SourceDraftRevision, LearningPlan Plan, TaskRequest Input,
    ResolvedTaskRequest ResolvedInput, TaskDocument Document, LengthMeasurement[] Measurements, int EngineRevision,
    Guid? TemplateVersionId, Guid? SourceSnapshotId, string CreatedByParentId, DateTime DraftCreatedAtUtc,
    string ReviewedByParentId, DateTime ReviewedAtUtc)
{
    internal static SnapshotPreview From(TaskSnapshot snapshot) => new(snapshot.Id, snapshot.SourceDraftId, snapshot.SourceDraftRevision,
        StoredJson.Read<LearningPlan>(snapshot.PlanJson), StoredJson.Read<TaskRequest>(snapshot.InputJson),
        StoredJson.Read<ResolvedTaskRequest>(snapshot.ResolvedInputJson), StoredJson.Read<TaskDocument>(snapshot.DocumentJson),
        StoredJson.Read<LengthMeasurement[]>(snapshot.MeasurementsJson), snapshot.EngineRevision,
        snapshot.TemplateVersionId, snapshot.SourceSnapshotId, snapshot.CreatedByParentId, snapshot.DraftCreatedAtUtc,
        snapshot.ReviewedByParentId, snapshot.ReviewedAtUtc);
}
