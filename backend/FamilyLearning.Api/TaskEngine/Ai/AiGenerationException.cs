namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Application-authored failure safe to expose without provider internals.</summary>
public sealed class AiGenerationException(int statusCode, string message, string? problemType = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    /// <summary>Optional stable ProblemDetails type for failures the client can distinguish safely.</summary>
    public string? ProblemType { get; } = problemType;
    /// <summary>Application validator paths, messages and measured counts; never provider errors or generated text.</summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }
    /// <summary>Safe call outcome, independent of whether the caller already persisted earlier stages.</summary>
    public string Category { get; init; } = "provider";
    /// <summary>Both provider slots are in use; an interactive caller retries, background work waits instead.</summary>
    internal static AiGenerationException Busy() => new(503, "שירות היצירה עסוק כרגע. אפשר לנסות שוב בעוד רגע.") { Category = "busy" };
    internal static AiGenerationException InputLimit(string category) => new(413,
        "הבקשה ליצירה גדולה מדי. יש לצמצם את ההגדרות או את התוכן.", "urn:family-learning:ai-input-limit")
    { Category = category };
    public static AiGenerationException InvalidOutput(IReadOnlyDictionary<string, string[]>? errors = null) => new(502,
        "התוכן שהתקבל לא עבר את בדיקות התקינות. אפשר לנסות שוב או לדייק את ההנחיות.",
        errors is null ? null : "urn:family-learning:ai-validation")
    { ValidationErrors = errors, Category = errors is null ? "invalid-output" : "validation" };
    public static AiGenerationException OutputLimit() => new(502,
        "המודל הגיע למגבלת הפלט לפני שהשלים את התוכן. אפשר לנסות שוב.",
        "urn:family-learning:ai-output-limit")
    { Category = "output-limit" };
}
