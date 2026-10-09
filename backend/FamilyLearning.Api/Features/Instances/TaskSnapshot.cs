namespace FamilyLearning.Api.Features.Instances;

/// <summary>Self-contained immutable parent-reviewed activity, independent of live drafts, templates and operation evidence.</summary>
/// <remarks>Provenance IDs deliberately have no live foreign keys. Assignments retain this row through restrictive ownership foreign keys.</remarks>
public sealed class TaskSnapshot(Guid familyId, Guid sourceDraftId, long sourceDraftRevision, string title,
    string planJson, string documentJson, string measurementsJson,
    int engineRevision, Guid? templateVersionId, Guid? sourceSnapshotId, string createdByParentId,
    DateTime draftCreatedAtUtc, string reviewedByParentId, DateTime reviewedAtUtc)
{
    public DateTime? ArchivedAtUtc { get; private set; }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; } = familyId;
    public Guid SourceDraftId { get; private set; } = sourceDraftId;
    public long SourceDraftRevision { get; private set; } = sourceDraftRevision;
    public string Title { get; private set; } = title;
    public string PlanJson { get; private set; } = planJson;
    public string DocumentJson { get; private set; } = documentJson;
    public string MeasurementsJson { get; private set; } = measurementsJson;
    public int EngineRevision { get; private set; } = engineRevision;
    public Guid? TemplateVersionId { get; private set; } = templateVersionId;
    public Guid? SourceSnapshotId { get; private set; } = sourceSnapshotId;
    public string CreatedByParentId { get; private set; } = createdByParentId;
    public DateTime DraftCreatedAtUtc { get; private set; } = draftCreatedAtUtc;
    public string ReviewedByParentId { get; private set; } = reviewedByParentId;
    public DateTime ReviewedAtUtc { get; private set; } = reviewedAtUtc;
}
