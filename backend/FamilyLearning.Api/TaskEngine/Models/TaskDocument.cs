using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Ai;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Shared parent-only editable/frozen content. Child DTOs must never expose these answer keys.</summary>
/// <remarks>Collections are mutable; engine boundaries return detached copies. Features own persistence and authorization.</remarks>
public sealed record TaskDocument(
    [property: JsonRequired] string Title,
    string? Instructions,
    [property: JsonRequired] MaterialContent[] Materials,
    [property: JsonRequired] DocumentQuestion[] Questions);

/// <summary>Accepted material with an app-owned revision; supplied bodies must match the resolved source exactly.</summary>
public sealed record MaterialContent(string Id, long Revision, string? Title, string Body, ContentOrigin Origin,
    ContentAcceptance? Acceptance);

/// <summary>A parent-editable question whose answer may be incomplete until release. IDs and evidence are app-owned.</summary>
public sealed record DocumentQuestion(string Id, string Prompt, QuestionInteraction Interaction, QuestionAnswer? Answer,
    int Points, ContentOrigin Origin, ContentAcceptance? Acceptance);

/// <summary>Original generation or manual/source provenance; adoption never rewrites this record.</summary>
public sealed record ContentOrigin(string Kind, int? EngineRevision = null, string? InputFingerprint = null,
    GenerationMetadata? Generation = null);

/// <summary>Current acceptance basis, independent of original provenance. No persisted stale/readiness flag is needed.</summary>
public sealed record ContentAcceptance(string InputFingerprint, MaterialRevision[] Sources, DateTime? AdoptedAtUtc = null);

/// <summary>An actual material included by the application in a question call.</summary>
public sealed record MaterialRevision(string Id, long Revision);

/// <summary>Untrusted generated-only material output; IDs must exactly match the selected generated requirements.</summary>
public sealed record MaterialCandidate([property: JsonRequired] string Id, string? Title, [property: JsonRequired] string Body);

/// <summary>An indivisible batch of generated materials, without supplied-source echoes.</summary>
public sealed record MaterialCandidateBatch([property: JsonRequired] MaterialCandidate[] Materials);

/// <summary>A complete untrusted question with no model-owned identity, revision or provenance.</summary>
public sealed record QuestionCandidate([property: JsonRequired] string Prompt,
    [property: JsonRequired] QuestionInteraction Interaction,
    [property: JsonRequired] QuestionAnswer? Answer,
    [property: JsonRequired] int Points);

/// <summary>The question stage owns task title/instructions and the complete ordered question batch.</summary>
public sealed record QuestionCandidateBatch([property: JsonRequired] string Title, string? Instructions,
    [property: JsonRequired] QuestionCandidate[] Questions);

/// <summary>Unsafe shape errors prevent saving; repairable diagnostics permit a draft but prevent release.</summary>
public sealed record DraftDocumentCheck(Dictionary<string, string[]> Errors, Dictionary<string, string[]> Diagnostics);

/// <summary>A whole accepted document, or an unapplied candidate and diagnostics. No partial material application is returned.</summary>
public sealed record MaterialAcceptance(TaskDocument? Document, MaterialCandidateBatch Candidate, Dictionary<string, string[]> Diagnostics);

/// <summary>Body-only word count for a material ID or total generated scope. Targets have no Boolean pass threshold.</summary>
public sealed record LengthMeasurement(string Scope, ResolvedLength Expected, int Actual, bool? Satisfied);

/// <summary>Describes the input control independently of the question's school subject.</summary>
public sealed record QuestionInteraction([property: JsonRequired] string Type, string[]? Options = null);
/// <summary>The expected answer; numeric answers use invariant-culture text.</summary>
public sealed record QuestionAnswer([property: JsonRequired] string Value);
