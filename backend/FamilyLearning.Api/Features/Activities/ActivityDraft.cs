namespace FamilyLearning.Api.Features.Activities;

/// <summary>Family-owned editable checkpoint. Revisions fence concurrent writes; release metadata remains after snapshot deletion.</summary>
public sealed class ActivityDraft(Guid familyId, string name, string planJson, string inputJson, string documentJson,
    Guid? templateVersionId, Guid? sourceSnapshotId, string createdByParentId)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; } = familyId;
    public string Name { get; private set; } = name;
    public string PlanJson { get; private set; } = planJson;
    public string InputJson { get; private set; } = inputJson;
    public string DocumentJson { get; private set; } = documentJson;
    public long Revision { get; private set; } = 1;
    public Guid? ActiveOperationId { get; private set; }
    public Guid? TemplateVersionId { get; private set; } = templateVersionId;
    public Guid? SourceSnapshotId { get; private set; } = sourceSnapshotId;
    public string CreatedByParentId { get; private set; } = createdByParentId;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public Guid? ReleasedSnapshotId { get; private set; }
    public long? ReleasedSourceRevision { get; private set; }

    /// <summary>Stages a validated edit or adoption. The caller must persist using the loaded EF concurrency token.</summary>
    internal void Save(string name, string plan, string input, string document)
    {
        if (ReleasedSnapshotId.HasValue) throw new InvalidOperationException("Released drafts are terminal.");
        Name = name;
        PlanJson = plan;
        InputJson = input;
        DocumentJson = document;
        Revision = checked(Revision + 1);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Stages the terminal marker in the same transaction as its immutable snapshot.</summary>
    internal void Release(Guid snapshotId, DateTime reviewedAtUtc)
    {
        if (ReleasedSnapshotId.HasValue || ActiveOperationId.HasValue) throw new InvalidOperationException("Draft cannot be released.");
        ReleasedSnapshotId = snapshotId;
        ReleasedSourceRevision = Revision;
        Revision = checked(Revision + 1);
        UpdatedAtUtc = reviewedAtUtc;
    }
}
