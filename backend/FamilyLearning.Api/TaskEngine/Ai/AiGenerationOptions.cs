using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Output caps and request deadlines shared by the engine and transport.</summary>
public sealed class AiGenerationOptions
{
    public const int DefaultMaxOutputTokens = 8192;
    public const int RequestByteLimit = 512 * 1024;
    public const int SchemaByteLimit = 64 * 1024;
    public const int OutputCharacterLimit = 32000;

    /// <summary>Serialized UTF-8 HTTP body limit, including every schema occurrence; may be lowered by configuration.</summary>
    public int MaxRequestBytes { get; set; } = RequestByteLimit;

    /// <summary>Serialized UTF-8 output-schema limit in every output mode; may be lowered by configuration.</summary>
    public int MaxSchemaBytes { get; set; } = SchemaByteLimit;

    /// <summary>Combined reasoning/output token ceiling per call; configuration validation accepts 1–32768.</summary>
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;

    /// <summary>json_schema, json_object or text; registration validates it. Only json_schema delivers the schema natively.</summary>
    public string ResponseFormat { get; set; } = "json_object";

    /// <summary>Also prompts with the schema in json_schema mode, for endpoints that enforce it without showing it to the model.</summary>
    public bool SchemaInPrompt { get; set; }

    /// <summary>Largest question batch whose exact count a json_schema endpoint accepts; larger batches rely on the prompt and validation.</summary>
    /// <remarks>Endpoint-specific: exact-count arrays consume the provider's schema budget. Validated within the product cap.</remarks>
    public int StrictQuestionCountLimit { get; set; } = EngineValidation.MaxQuestionCount;

    /// <summary>True when the provider receives and enforces the schema natively.</summary>
    public bool StrictSchema => ResponseFormat == "json_schema";

    /// <summary>Per-call deadline; configuration validation accepts 1–300 seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 180;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
}
