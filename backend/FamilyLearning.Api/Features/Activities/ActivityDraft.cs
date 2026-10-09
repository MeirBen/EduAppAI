using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Family-owned editable checkpoint. Revisions fence concurrent writes; release metadata remains after snapshot deletion.</summary>
public sealed class ActivityDraft(Guid familyId, string name, string planJson, string documentJson,
    Guid? templateVersionId, Guid? sourceSnapshotId, string createdByParentId)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; } = familyId;
    public string Name { get; private set; } = name;
    public string PlanJson { get; private set; } = planJson;
    public string DocumentJson { get; private set; } = documentJson;
    public string ChatJson { get; private set; } = "[]";
    public string? UndoJson { get; private set; }
    public long Revision { get; private set; } = 1;
    public Guid? ActiveOperationId { get; private set; }
    public Guid? TemplateVersionId { get; private set; } = templateVersionId;
    public Guid? SourceSnapshotId { get; private set; } = sourceSnapshotId;
    public string CreatedByParentId { get; private set; } = createdByParentId;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public Guid? ReleasedSnapshotId { get; private set; }
    public long? ReleasedSourceRevision { get; private set; }

    internal LearningPlan Plan => StoredJson.Read<LearningPlan>(PlanJson);
    internal TaskDocument Document => StoredJson.Read<TaskDocument>(DocumentJson);
    internal ActivityChatTurn[] Chat => StoredJson.Read<ActivityChatTurn[]>(ChatJson);
    internal ActivityUndo? Undo => UndoJson is null ? null : StoredJson.Read<ActivityUndo>(UndoJson);
    internal bool CanUndo => ActiveOperationId is null && ReleasedSnapshotId is null && Undo?.ResultingRevision == Revision;

    internal void ImportChat(ImportedChatTurn[]? turns) => ChatJson = StoredJson.Write(ActivityChat.Import(turns, Plan));

    internal void AppendTurn(ActivityChatTurn turn) => ChatJson = StoredJson.Write(ActivityChat.Append(Chat, turn));

    internal void CompleteChat(GenerationOperation operation, DateTime now, string? reply = null, string[]? assumptions = null)
    {
        if (Chat.Any(t => t.Role == "assistant" && t.OperationId == operation.Id)) return;
        var text = reply ?? operation.Status switch
        {
            "completed" => "הפעילות עודכנה. אפשר לעיין בתוצאה לפני אישור.",
            "cancelled" => "הפעולה נעצרה. התוכן השמור לא השתנה.",
            "unknown" => "הפעולה הופסקה ותוצאת קריאת ה־AI אינה ידועה. התוכן השמור לא השתנה.",
            "conflict" => "הפעולה לא הוחלה כי מצב הטיוטה השתנה.",
            _ => "הפעולה לא הושלמה. התוכן השמור לא השתנה."
        };
        AppendTurn(new("assistant", text, now, OperationId: operation.Id, Assumptions: assumptions, Outcome: operation.Status));
    }

    /// <summary>Stages the active reference; its concurrency token fences release without advancing the content revision.</summary>
    internal void StartOperation(Guid operationId) => ActiveOperationId = operationId;

    internal void ClearOperation(Guid operationId, DateTime now)
    {
        if (ActiveOperationId != operationId) return;
        ActiveOperationId = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Stages a validated edit or adoption. The caller must persist using the loaded EF concurrency token.</summary>
    internal void Save(string name, string plan, string document)
    {
        if (ReleasedSnapshotId.HasValue) throw new InvalidOperationException("Released drafts are terminal.");
        UndoJson = null;
        if (PlanJson == plan && DocumentJson == document) return;
        Name = name;
        PlanJson = plan;
        DocumentJson = document;
        Revision = checked(Revision + 1);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    internal bool ApplyOperation(LearningPlan plan, TaskDocument document, string kind)
    {
        var planJson = StoredJson.Write(plan);
        var documentJson = StoredJson.Write(document);
        if (PlanJson == planJson && DocumentJson == documentJson) return false;
        var previous = new ActivityUndo(Plan, Document, checked(Revision + 1));
        Save(plan.Name, planJson, documentJson);
        if (kind is "Revise" or "GenerateQuestions") UndoJson = StoredJson.Write(previous);
        return true;
    }

    internal void RestoreUndo(DateTime now)
    {
        if (!CanUndo) throw new InvalidOperationException("Undo is not available at this revision.");
        var undo = Undo!;
        Save(undo.Plan.Name, StoredJson.Write(undo.Plan), StoredJson.Write(undo.Document));
        AppendTurn(new("assistant", "השינוי האחרון בוטל והתוכן הקודם שוחזר.", now));
    }

    /// <summary>Stages the terminal marker in the same transaction as its immutable snapshot.</summary>
    internal void Release(Guid snapshotId, DateTime reviewedAtUtc)
    {
        if (ReleasedSnapshotId.HasValue || ActiveOperationId.HasValue) throw new InvalidOperationException("Draft cannot be released.");
        ReleasedSnapshotId = snapshotId;
        ReleasedSourceRevision = Revision;
        UndoJson = null;
        UpdatedAtUtc = reviewedAtUtc;
    }
}
