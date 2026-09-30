using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Presence-preserving create envelope: plan/input with optional template provenance, or snapshotId exclusively.</summary>
public sealed record CreateActivityRequest(JsonElement Plan = default, JsonElement Input = default,
    JsonElement TemplateId = default, JsonElement ExpectedVersion = default, JsonElement SnapshotId = default);

/// <summary>Complete editable checkpoint; metadata, material revisions and acceptance evidence cannot be submitted.</summary>
public sealed record SaveActivityRequest([property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] LearningPlan Plan, [property: JsonRequired] TaskRequest Input,
    [property: JsonRequired] EditableActivity Document);

/// <summary>Parent-editable content without application-owned revisions or acceptance evidence.</summary>
public sealed record EditableActivity([property: JsonRequired] string Title, string? Instructions,
    [property: JsonRequired] EditableMaterial[] Materials, [property: JsonRequired] EditableQuestion[] Questions);
/// <summary>Edits a plan material; supplied source text remains authoritative in the plan/input.</summary>
public sealed record EditableMaterial([property: JsonRequired] string Id, string? Title, [property: JsonRequired] string Body);
/// <summary>Retains an existing question ID; null requests a new server-assigned ID. Incomplete answers may be saved.</summary>
public sealed record EditableQuestion(string? Id, [property: JsonRequired] string Prompt,
    [property: JsonRequired] QuestionInteraction Interaction, QuestionAnswer? Answer, [property: JsonRequired] int Points);

/// <summary>Explicit inspection of selected content under the current requirements; cannot waive strict validation.</summary>
public sealed record AdoptActivityRequest([property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string[] MaterialIds, [property: JsonRequired] string[] QuestionIds);

/// <summary>Releasing this exact saved revision records the parent's review action.</summary>
public sealed record ReleaseActivityRequest([property: JsonRequired] long ExpectedRevision);

/// <summary>Bounded library projection; answers and content remain in the owned detail route.</summary>
public sealed record ActivitySummary(Guid Id, string Name, long Revision, bool Released, DateTime UpdatedAtUtc);

/// <summary>Saved parent-only state. Diagnostics are derived from authoritative data, never an independent readiness cache.</summary>
public sealed record ActivityDetail(Guid Id, long Revision, LearningPlan Plan, TaskRequest Input, TaskDocument Document,
    IReadOnlyDictionary<string, string[]> Diagnostics, Guid? ActiveOperationId, Guid? TemplateVersionId,
    Guid? ReleasedSnapshotId, long? ReleasedSourceRevision, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    internal static ActivityDetail From(ActivityDraft draft)
    {
        var plan = StoredJson.Read<LearningPlan>(draft.PlanJson);
        var input = StoredJson.Read<TaskRequest>(draft.InputJson);
        var document = StoredJson.Read<TaskDocument>(draft.DocumentJson);
        var resolved = TaskRequestResolver.Resolve(plan, input).Value ?? throw new InvalidOperationException("Invalid stored activity input.");
        var check = TaskDocumentValidator.ValidateDraft(resolved, document);
        return new(draft.Id, draft.Revision, plan, input, document, check.Diagnostics, draft.ActiveOperationId,
            draft.TemplateVersionId, draft.ReleasedSnapshotId, draft.ReleasedSourceRevision, draft.CreatedAtUtc, draft.UpdatedAtUtc);
    }
}
