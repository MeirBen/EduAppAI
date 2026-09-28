namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>A safe, localized failure that may be returned to the parent without provider internals.</summary>
public sealed class AiGenerationException(int statusCode, string message, string? problemType = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    /// <summary>Optional stable ProblemDetails type for failures the client can distinguish safely.</summary>
    public string? ProblemType { get; } = problemType;
    public static AiGenerationException InvalidOutput() => new(502,
        "התוכן שהתקבל לא עבר את בדיקות התקינות. לא נשמר דבר. אפשר לנסות שוב או לדייק את ההנחיות.");
    public static AiGenerationException OutputLimit() => new(502,
        "המודל הגיע למגבלת הפלט לפני שהשלים את התוכן. לא נשמר דבר. אפשר לנסות שוב.",
        "urn:family-learning:ai-output-limit");
}

/// <summary>Diagnostics only: no prompt, identity, answers, reasoning or credentials.</summary>
public sealed record GenerationMetadata(string Provider, string Model, string PromptVersion, DateTime GeneratedAtUtc);

/// <summary>A validated transient result. Only the calling parent workflow may decide to persist it.</summary>
public sealed record AiResult<T>(T Value, GenerationMetadata Metadata);
