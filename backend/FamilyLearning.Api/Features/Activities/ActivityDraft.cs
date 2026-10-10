using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Family-owned editable checkpoint. Revisions fence concurrent writes; release metadata remains after snapshot deletion.</summary>
public sealed class ActivityDraft(Guid id, Guid familyId, string name, string planJson, string documentJson,
    Guid? sourceSnapshotId, string createdByParentId)
{
    /// <summary>Chosen by the creating client, so a retried creation finds this draft instead of adding another.</summary>
    public Guid Id { get; private set; } = id;
    public Guid FamilyId { get; private set; } = familyId;
    public string Name { get; private set; } = name;
    public string PlanJson { get; private set; } = planJson;
    public string DocumentJson { get; private set; } = documentJson;
    public string ChatJson { get; private set; } = "[]";
    public string? UndoJson { get; private set; }
    public long Revision { get; private set; } = 1;
    public Guid? ActiveOperationId { get; private set; }
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

    /// <summary>Records the operation's single reply: the model's own text, else its applied changes, else its outcome.</summary>
    internal void CompleteChat(GenerationOperation operation, DateTime now, string? reply = null, string[]? assumptions = null,
        string[]? changes = null)
    {
        if (Chat.Any(t => t.Role == "assistant" && t.OperationId == operation.Id)) return;
        var summary = reply is null ? changes : null;
        var text = reply ?? (summary is not null ? "" : operation.Status switch
        {
            "completed" => "הפעולה הסתיימה ללא שינוי בתוכן.",
            "cancelled" => "הפעולה נעצרה. התוכן השמור לא השתנה.",
            "unknown" => "הפעולה הופסקה ותוצאת קריאת ה־AI אינה ידועה. התוכן השמור לא השתנה.",
            "conflict" => "הפעולה לא הוחלה כי מצב הטיוטה השתנה.",
            _ => "הפעולה לא הושלמה. התוכן השמור לא השתנה."
        });
        AppendTurn(new("assistant", text, now, OperationId: operation.Id, Assumptions: assumptions, Outcome: operation.Status,
            Changes: summary));
    }

    /// <summary>Stages the active reference; its concurrency token fences release without advancing the content revision.</summary>
    internal void StartOperation(Guid operationId) => ActiveOperationId = operationId;

    internal void ClearOperation(Guid operationId, DateTime now)
    {
        if (ActiveOperationId != operationId) return;
        ActiveOperationId = null;
        UpdatedAtUtc = now;
    }

    /// <summary>The library lists a draft by its learner title once content has one, and by its plan name before.</summary>
    internal static string LibraryName(LearningPlan plan, TaskDocument document) =>
        string.IsNullOrWhiteSpace(document.Title) ? plan.Name : document.Title;

    /// <summary>Stages a validated edit or adoption and ends any undo. The caller must persist using the loaded EF concurrency token.</summary>
    internal void Save(LearningPlan plan, TaskDocument document)
    {
        UndoJson = null;
        Stage(plan, document);
    }

    /// <summary>Stages the accepted content and returns its factual change statements; null means there was no saved change.</summary>
    internal string[]? ApplyOperation(LearningPlan plan, TaskDocument document, string kind)
    {
        var previousPlan = Plan;
        var previousDocument = Document;
        if (!Stage(plan, document)) return null;
        UndoJson = kind is "Revise" or "GenerateQuestions" ? StoredJson.Write(new ActivityUndo(previousPlan, previousDocument, Revision)) : null;
        return ActivityChangeNotice.Describe(previousPlan, previousDocument, plan, document);
    }

    internal void RestoreUndo(DateTime now)
    {
        if (!CanUndo) throw new InvalidOperationException("Undo is not available at this revision.");
        var undo = Undo!;
        Save(undo.Plan, undo.Document);
        AppendTurn(new("assistant", "השינוי האחרון בוטל והתוכן הקודם שוחזר.", now));
    }

    /// <summary>Writes changed content under a new revision; false means nothing changed and nothing was written.</summary>
    private bool Stage(LearningPlan plan, TaskDocument document)
    {
        if (ReleasedSnapshotId.HasValue) throw new InvalidOperationException("Released drafts are terminal.");
        var planJson = StoredJson.Write(plan);
        var documentJson = StoredJson.Write(document);
        if (PlanJson == planJson && DocumentJson == documentJson) return false;
        Name = LibraryName(plan, document);
        PlanJson = planJson;
        DocumentJson = documentJson;
        Revision = checked(Revision + 1);
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
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
