using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>A durable operation/key tombstone. Only bulky artifacts expire; draft deletion owns its lifetime.</summary>
public sealed class GenerationOperation
{
    private GenerationOperation() { }

    internal GenerationOperation(ActivityDraft draft, StartGenerationRequest request, ResolvedTaskRequest input,
        TaskDocument document, string stage, string profileFingerprint, DateTime now)
    {
        FamilyId = draft.FamilyId;
        DraftId = draft.Id;
        OperationKey = request.OperationKey;
        RequestFingerprint = Fingerprint(draft.Id, request);
        InputFingerprint = TaskRequestResolver.Fingerprint(input);
        ProfileFingerprint = profileFingerprint;
        OriginalRevision = ExpectedRevision = draft.Revision;
        Kind = request.Kind;
        Stage = stage;
        ArtifactsJson = StoredJson.Write(new GenerationArtifacts(input, document, request.TargetId, request.Instruction, []));
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FamilyId { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid OperationKey { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public string InputFingerprint { get; private set; } = "";
    public string ProfileFingerprint { get; private set; } = "";
    public int EngineRevision { get; private set; } = EngineVersions.Revision;
    public int SchemaVersion { get; private set; } = EngineVersions.SchemaVersion;
    public long OriginalRevision { get; private set; }
    public long ExpectedRevision { get; private set; }
    public string Kind { get; private set; } = "";
    public string Stage { get; private set; } = "";
    public string Status { get; private set; } = "queued";
    public string? Failure { get; private set; }
    public string? ArtifactsJson { get; private set; }
    public string StepsJson { get; private set; } = "[]";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }

    internal GenerationArtifacts Artifacts => StoredJson.Read<GenerationArtifacts>(ArtifactsJson ?? throw new InvalidOperationException("Active operation artifacts are missing."));
    internal GenerationStep[] Steps => StoredJson.Read<GenerationStep[]>(StepsJson);

    internal bool StoreArtifacts(GenerationArtifacts artifacts, GenerationStep[] steps)
    {
        var json = StoredJson.Write(artifacts);
        var summaries = StoredJson.Write(steps);
        if (steps.Length > GenerationOperationOptions.StepLimit || artifacts.Steps.Length != steps.Length ||
            Encoding.UTF8.GetByteCount(summaries) > GenerationOperationOptions.SummaryByteLimit ||
            Encoding.UTF8.GetByteCount(json) > GenerationOperationOptions.EvidenceByteLimit - GenerationOperationOptions.SummaryByteLimit) return false;
        ArtifactsJson = json;
        StepsJson = summaries;
        return true;
    }

    internal void Claim() => Status = "calling";
    internal void QueueQuestions(long revision)
    {
        ExpectedRevision = revision;
        Stage = "questions";
        Status = "queued";
    }

    internal void Finish(string status, string? failure, DateTime now)
    {
        Status = status;
        Failure = failure;
        FinishedAtUtc = now;
    }

    internal void MarkInterruptedStep(string outcome)
    {
        var steps = Steps;
        if (steps.Length > 0 && steps[^1].Outcome == "calling") steps[^1] = steps[^1] with { Outcome = outcome };
        StepsJson = StoredJson.Write(steps);
    }

    internal void RecordLateUsage(GenerationStep step)
    {
        var steps = Steps;
        if (steps.Length == 0) return;
        RecordResult(steps[^1] with { Outcome = "cancelled", Usage = step.Usage ?? steps[^1].Usage, Metadata = step.Metadata ?? steps[^1].Metadata });
    }

    internal void RecordResult(GenerationStep step)
    {
        var steps = Steps;
        steps[^1] = step;
        StepsJson = StoredJson.Write(steps);
        if (Encoding.UTF8.GetByteCount(StepsJson) > GenerationOperationOptions.SummaryByteLimit)
            throw new InvalidOperationException("Call summaries exceeded their reserved budget.");
    }

    internal static string Fingerprint(Guid draftId, StartGenerationRequest request) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { draftId, request.ExpectedRevision, request.Kind, request.TargetId, request.Instruction }, EngineJson.Options)));
}

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
