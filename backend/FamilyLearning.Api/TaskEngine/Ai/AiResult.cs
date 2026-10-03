namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Diagnostics only: no prompt, identity, answers, reasoning or credentials.</summary>
public sealed record GenerationMetadata(string Provider, string Model, string PromptVersion, DateTime GeneratedAtUtc,
    int? EngineRevision = null, int? SchemaVersion = null);

/// <summary>Validated output and generation metadata; persistence belongs to the caller.</summary>
public sealed record AiResult<T>(T Value, GenerationMetadata Metadata);
