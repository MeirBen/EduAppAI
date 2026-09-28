namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Bounded provider-call deadline, shared by generation and its transport.</summary>
public sealed class AiGenerationOptions
{
    /// <summary>Defaults to three minutes; startup validation accepts 1–300 seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
}
