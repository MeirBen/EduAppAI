using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Frozen parent-owned revision context; persistence evidence is projected out before provider access.</summary>
public sealed record ActivityRevisionInput(LearningPlan Plan, TaskDocument Current, string Message,
    RevisionTarget? Target = null, ConfirmedSource[]? Sources = null, RevisionTurn[]? Context = null);

/// <summary>An optional existing material or question selected by the parent.</summary>
public sealed record RevisionTarget(string Kind, string Id);

/// <summary>Exact source text explicitly confirmed before starting an operation.</summary>
public sealed record ConfirmedSource([property: JsonRequired] string Label, [property: JsonRequired] string Text);

/// <summary>Bounded conversation for interpretation; outcomes distinguish applied requests from failures.</summary>
public sealed record RevisionTurn(string Role, string Text, RevisionTarget? Target = null, string? Outcome = null);

/// <summary>A planner answer, clarification or complete proposed change; exactly one member is non-null.</summary>
public sealed record RevisionDecision([property: JsonRequired] string? Answer,
    [property: JsonRequired] string? Clarification, [property: JsonRequired] RevisionChange? Change);

/// <summary>Concrete requirements and bounded one-off edits, never an execution queue or completion claim.</summary>
public sealed record RevisionChange([property: JsonRequired] LearningPlan Plan,
    [property: JsonRequired] string[] Assumptions, [property: JsonRequired] ContentEdit[] MaterialEdits,
    [property: JsonRequired] QuestionEdits Questions, [property: JsonRequired] string[]? QuestionOrder);

/// <summary>One existing target and a self-contained instruction.</summary>
public sealed record ContentEdit([property: JsonRequired] string Id, [property: JsonRequired] string Instruction);

/// <summary>Question intent; deterministic scope may expand work when requirements change.</summary>
public sealed record QuestionEdits([property: JsonRequired] string Scope, [property: JsonRequired] string? Instruction,
    [property: JsonRequired] ContentEdit[] Items);

internal sealed record RevisionCandidate([property: JsonRequired] RevisionDecision Result);
