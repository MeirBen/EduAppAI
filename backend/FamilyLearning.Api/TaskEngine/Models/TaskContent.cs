using System.Text.Json.Serialization;

namespace FamilyLearning.Api.TaskEngine.Models;

/// <summary>AI-generated task content and parent-only answer keys.</summary>
/// <remarks>Read the saved snapshot without regeneration. Child responses must omit answers.</remarks>
public sealed record TaskContent(
    [property: JsonRequired] string Title,
    string? Instructions,
    [property: JsonRequired] ContentBlock[] ContentBlocks,
    [property: JsonRequired] TaskQuestion[] Questions);

/// <summary>Plain-text material presented before the questions.</summary>
public sealed record ContentBlock([property: JsonRequired] string Type, [property: JsonRequired] string Text);
/// <summary>A question with an ID local to its content snapshot and an integer point value.</summary>
public sealed record TaskQuestion(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Prompt,
    [property: JsonRequired] QuestionInteraction Interaction,
    [property: JsonRequired] QuestionAnswer Answer,
    [property: JsonRequired] int Points);
/// <summary>Describes the input control independently of the question's school subject.</summary>
public sealed record QuestionInteraction([property: JsonRequired] string Type, string[]? Options = null);
/// <summary>The expected answer; numeric answers use invariant-culture text.</summary>
public sealed record QuestionAnswer([property: JsonRequired] string Value);
