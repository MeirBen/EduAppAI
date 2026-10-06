using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>Resolved requirements and prior materials for batch selection/strict acceptance; no chat or persistence identity.</summary>
public sealed record MaterialGenerationInput(ResolvedTaskRequest Request, MaterialContent[] Materials);

/// <summary>Resolved requirements and exact accepted materials; every source revision becomes a question dependency.</summary>
public sealed record QuestionGenerationInput(ResolvedTaskRequest Request, MaterialContent[] Materials);

/// <summary>An application-selected generated material. Current content supports aggregate safety checks, not unrestricted model edits.</summary>
public sealed record MaterialReplacementInput(ResolvedTaskRequest Request, TaskDocument Current, string MaterialId, string? Instruction = null);

/// <summary>An application-selected question; the rest of the activity is read-only context and app-owned identities never enter the provider request.</summary>
public sealed record QuestionReplacementInput(ResolvedTaskRequest Request, TaskDocument Current, string QuestionId, string? Instruction = null);

/// <summary>Untrusted generated-only material output; IDs must exactly match the selected generated requirements.</summary>
public sealed record MaterialCandidate([property: JsonRequired] string Id, string? Title, [property: JsonRequired] string Body);

/// <summary>The premise and causal or explanatory structure a generated batch is written from; private, never learner content.</summary>
public sealed record MaterialIdea([property: JsonRequired] string Premise, [property: JsonRequired] string Structure);

/// <summary>An untrusted proposed idea and the model's estimate, from 0 to 100, of its overlap with recent family ideas.</summary>
public sealed record MaterialIdeaCandidate([property: JsonRequired] MaterialIdea Idea, [property: JsonRequired] int RecentOverlap);

/// <summary>Untrusted alternatives; the application validates them and selects one before writing.</summary>
public sealed record MaterialIdeaCandidateBatch([property: JsonRequired] MaterialIdeaCandidate[] Ideas);

/// <summary>An indivisible batch of generated materials, without supplied-source echoes.</summary>
public sealed record MaterialCandidateBatch([property: JsonRequired] MaterialCandidate[] Materials);

/// <summary>A complete untrusted question with no model-owned identity, revision or provenance.</summary>
public sealed record QuestionCandidate([property: JsonRequired] string Prompt,
    [property: JsonRequired] QuestionInteraction Interaction,
    [property: JsonRequired] QuestionAnswer? Answer,
    [property: JsonRequired] int Points)
{
    /// <summary>Removes surrounding whitespace the model emits around short values (observed as "= "); content is otherwise untouched.</summary>
    public QuestionCandidate Trimmed() => new(Prompt?.Trim()!, Interaction is null ? null! : Interaction with { Options = Interaction.Options?.Select(option => option?.Trim()!).ToArray() },
        Answer is null ? null : Answer with { Value = Answer.Value?.Trim()! }, Points);
}

/// <summary>The question stage owns task title/instructions and the complete ordered question batch.</summary>
public sealed record QuestionCandidateBatch([property: JsonRequired] string Title, string? Instructions,
    [property: JsonRequired] QuestionCandidate[] Questions)
{
    public QuestionCandidateBatch Trimmed() => new(Title?.Trim()!, Instructions?.Trim(), Questions?.Select(question => question?.Trimmed()!).ToArray()!);
}

/// <summary>A whole accepted document, or an unapplied candidate and diagnostics. No partial material application is returned.</summary>
public sealed record MaterialAcceptance(TaskDocument? Document, MaterialCandidateBatch Candidate, Dictionary<string, string[]> Diagnostics);
