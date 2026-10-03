using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Original request identity; replay is compared before checking the draft's current revision.</summary>
public sealed record StartGenerationRequest([property: JsonRequired] Guid OperationKey, [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string Kind, string? TargetId = null, string? Instruction = null);

/// <summary>Pinned requirements and stage inputs. These private parent diagnostics expire after terminal retention.</summary>
public sealed record GenerationArtifacts(ResolvedTaskRequest Input, TaskDocument Current, string? TargetId, string? Instruction,
    GenerationStepArtifact[] Steps);

/// <summary>Exact document/revisions captured with the durable claim, before a provider can run.</summary>
public sealed record GenerationStepArtifact(string Stage, TaskDocument Document, AiCallEvidence? Call = null,
    JsonElement? Candidate = null, IReadOnlyDictionary<string, string[]>? Diagnostics = null);

/// <summary>Minimal durable stage outcome, separate from expiring prompts and output.</summary>
public sealed record GenerationStep(string Stage, string Outcome, AiCallUsage? Usage = null, GenerationMetadata? Metadata = null);

/// <summary>Parent-only operation state. Completion does not assert that the activity is ready for release.</summary>
public sealed record GenerationOperationDetail(Guid Id, Guid DraftId, string Kind, string Status, string Stage,
    long OriginalRevision, long ExpectedRevision, string? Failure, bool DiagnosticsExpired, GenerationStep[] Steps, GenerationArtifacts? Artifacts)
{
    internal static GenerationOperationDetail From(GenerationOperation operation) => new(operation.Id, operation.DraftId,
        operation.Kind, operation.Status, operation.Stage, operation.OriginalRevision, operation.ExpectedRevision, operation.Failure,
        operation.ArtifactsJson is null, operation.Steps, operation.ArtifactsJson is { } json ? StoredJson.Read<GenerationArtifacts>(json) : null);
}
