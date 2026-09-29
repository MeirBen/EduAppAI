namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Output caps and request deadlines shared by the engine and transport.</summary>
public sealed class AiGenerationOptions
{
    public const int DefaultMaxOutputTokens = 8192;

    /// <summary>Combined reasoning/output token ceiling per call; configuration validation accepts 1–32768.</summary>
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;

    /// <summary>Per-call deadline; configuration validation accepts 1–300 seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
}
