namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>A safe, localized failure that may be returned to the parent without provider internals.</summary>
public sealed class AiGenerationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public static AiGenerationException InvalidOutput() => new(502,
        "התוכן שהתקבל לא עבר את בדיקות התקינות. לא נשמר דבר. אפשר לנסות שוב או לדייק את ההנחיות.");
}

/// <summary>Diagnostics only: no prompt, identity, answers, reasoning or credentials.</summary>
public sealed record GenerationMetadata(string Provider, string Model, string PromptVersion, DateTime GeneratedAtUtc);

/// <summary>A validated transient result. Only the calling parent workflow may decide to persist it.</summary>
public sealed record AiResult<T>(T Value, GenerationMetadata Metadata);
