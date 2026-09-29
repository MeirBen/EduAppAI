namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Generation limits shared by generation and its transport.</summary>
public sealed class AiGenerationOptions
{
    /// <summary>Default combined reasoning and final-output token ceiling per provider call.</summary>
    public const int DefaultMaxOutputTokens = 8192;

    /// <summary>Per-call token ceiling; startup validation accepts 1–32768.</summary>
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;

    /// <summary>Defaults to three minutes; startup validation accepts 1–300 seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
}
