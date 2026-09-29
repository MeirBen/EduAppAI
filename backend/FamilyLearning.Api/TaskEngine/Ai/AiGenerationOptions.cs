namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Generation limits shared by generation and its transport.</summary>
public sealed class AiGenerationOptions
{
    /// <summary>Combined reasoning and final-output token ceiling per provider call.</summary>
    public const int MaxOutputTokens = 8192;

    /// <summary>Defaults to three minutes; startup validation accepts 1–300 seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
}
