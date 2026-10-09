using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Original request identity; replay is compared before checking the draft's current revision.</summary>
public sealed record StartGenerationRequest([property: JsonRequired] Guid OperationKey, [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string Kind, string? TargetId = null, string? Instruction = null,
    string? Message = null, RevisionTarget? Target = null, ConfirmedSource[]? Sources = null);

/// <summary>Pinned requirements and stage inputs. These private parent diagnostics expire after terminal retention.</summary>
/// <remarks><see cref="SelectedIdea"/> is set by the material-ideas checkpoint and read by material writing.</remarks>
public sealed record GenerationArtifacts(ResolvedTaskRequest Input, TaskDocument Current, string? TargetId, string? Instruction,
    GenerationStepArtifact[] Steps, GenerationHistory History, MaterialIdea? SelectedIdea,
    LearningPlan Plan, RevisionWork? Scope = null, int RewriteIndex = 0, int QuestionIndex = 0,
    RevisionTurn[]? Context = null, ConfirmedSource[]? Sources = null, RevisionTarget? Target = null,
    string? Reply = null, string[]? Assumptions = null);

/// <summary>Recent family ideas and question prompts captured once at admission, without identities or answer keys.</summary>
/// <remarks>Idea generation receives only <see cref="Ideas"/> and question generation only <see cref="Questions"/>.</remarks>
public sealed record GenerationHistory(MaterialIdea[] Ideas, string[] Questions);

/// <summary>Exact document/revisions captured with the durable claim, before a provider can run.</summary>
public sealed record GenerationStepArtifact(string Stage, TaskDocument Document, AiCallEvidence? Call = null,
    JsonElement? Candidate = null, IReadOnlyDictionary<string, string[]>? Diagnostics = null);

/// <summary>Minimal durable stage outcome, separate from expiring prompts and output.</summary>
public sealed record GenerationStep(string Stage, string Outcome, AiCallUsage? Usage = null, GenerationMetadata? Metadata = null);

/// <summary>Parent-only operation state. ExpectedRevision is its last owned draft revision; completion does not assert readiness for release.</summary>
public sealed record GenerationOperationDetail(Guid Id, Guid DraftId, string Kind, string Status, string Stage,
    long OriginalRevision, long ExpectedRevision, string? Failure, bool DiagnosticsExpired, GenerationStep[] Steps, GenerationArtifacts? Artifacts)
{
    internal static GenerationOperationDetail From(GenerationOperation operation) => new(operation.Id, operation.DraftId,
        operation.Kind, operation.Status, operation.Stage, operation.OriginalRevision, operation.ExpectedRevision, operation.Failure,
        operation.ArtifactsJson is null, operation.Steps, operation.ArtifactsJson is { } json ? StoredJson.Read<GenerationArtifacts>(json) : null);
}
