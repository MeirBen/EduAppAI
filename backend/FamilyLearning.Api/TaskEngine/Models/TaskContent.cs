namespace FamilyLearning.Api.TaskEngine.Models;

public sealed record TaskContent(
    string Title,
    string? Instructions,
    ContentBlock[] ContentBlocks,
    TaskQuestion[] Questions);

public sealed record ContentBlock(string Type, string Text);
public sealed record TaskQuestion(
    string Id, string Prompt, QuestionInteraction Interaction, QuestionAnswer Answer, int Points);
public sealed record QuestionInteraction(string Type, string[]? Options = null);
public sealed record QuestionAnswer(string Value);
