using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Ai;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Shared parent-only editable/frozen content. Child DTOs must never expose these answer keys.</summary>
/// <remarks>Treat collections as immutable; engine results may share unchanged arrays. Features own persistence and authorization.</remarks>
public sealed record TaskDocument(
    [property: JsonRequired] string Title,
    string? Instructions,
    [property: JsonRequired] MaterialContent[] Materials,
    [property: JsonRequired] DocumentQuestion[] Questions);

/// <summary>Accepted material with an app-owned revision; supplied bodies must match the resolved source exactly.</summary>
/// <remarks><see cref="Idea"/> is generation provenance: manual edits and polish retain it; an AI rewrite clears it only when the title or body changes.</remarks>
public sealed record MaterialContent(string Id, long Revision, string? Title, string Body, ContentOrigin Origin,
    ContentAcceptance? Acceptance, MaterialIdea? Idea = null);

/// <summary>A parent-editable question whose answer may be incomplete until release. IDs and evidence are app-owned.</summary>
public sealed record DocumentQuestion(string Id, string Prompt, QuestionInteraction Interaction, QuestionAnswer? Answer,
    int Points, ContentOrigin Origin, ContentAcceptance? Acceptance);

/// <summary>Describes the input control independently of the question's school subject.</summary>
public sealed record QuestionInteraction([property: JsonRequired] string Type, string[]? Options = null);

/// <summary>The expected answer; numeric answers use invariant-culture text.</summary>
public sealed record QuestionAnswer([property: JsonRequired] string Value);

/// <summary>Original generation or manual/source provenance; adoption never rewrites this record.</summary>
public sealed record ContentOrigin(string Kind, int? EngineRevision = null, string? InputFingerprint = null,
    GenerationMetadata? Generation = null);

/// <summary>Current acceptance basis, independent of original provenance. No persisted stale/readiness flag is needed.</summary>
public sealed record ContentAcceptance(string InputFingerprint, MaterialRevision[] Sources, DateTime? AdoptedAtUtc = null);

/// <summary>A material revision used as a question's source or current acceptance basis.</summary>
public sealed record MaterialRevision(string Id, long Revision);

/// <summary>Unsafe shape errors prevent saving; repairable diagnostics permit a draft but prevent release.</summary>
public sealed record DraftDocumentCheck(Dictionary<string, string[]> Errors, Dictionary<string, string[]> Diagnostics);

/// <summary>Body-only word count for a material ID or total generated scope. Targets have no Boolean pass threshold.</summary>
public sealed record LengthMeasurement(string Scope, ResolvedLength Expected, int Actual, bool? Satisfied);
