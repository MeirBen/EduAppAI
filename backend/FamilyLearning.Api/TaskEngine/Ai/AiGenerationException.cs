namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-authored failure safe to expose without provider internals.</summary>
public sealed class AiGenerationException(int statusCode, string message, string? problemType = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    /// <summary>Optional stable ProblemDetails type for failures the client can distinguish safely.</summary>
    public string? ProblemType { get; } = problemType;
    /// <summary>Application validator paths and fixed messages for diagnostics; never provider errors or generated values.</summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }
    public static AiGenerationException InvalidOutput(IReadOnlyDictionary<string, string[]>? errors = null) => new(502,
        "התוכן שהתקבל לא עבר את בדיקות התקינות. לא נשמר דבר. אפשר לנסות שוב או לדייק את ההנחיות.")
    { ValidationErrors = errors };
    public static AiGenerationException OutputLimit() => new(502,
        "המודל הגיע למגבלת הפלט לפני שהשלים את התוכן. לא נשמר דבר. אפשר לנסות שוב.",
        "urn:family-learning:ai-output-limit");
}

/// <summary>Diagnostics only: no prompt, identity, answers, reasoning or credentials.</summary>
public sealed record GenerationMetadata(string Provider, string Model, string PromptVersion, DateTime GeneratedAtUtc);

/// <summary>Validated output and generation metadata; persistence belongs to the caller.</summary>
public sealed record AiResult<T>(T Value, GenerationMetadata Metadata);
