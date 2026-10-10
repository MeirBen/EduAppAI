using System.Text.Json;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Per-call observer owned by one caller. Never share it across calls; it retains private diagnostic text, not reasoning.</summary>
public sealed class AiCallEvidence
{
    internal const int IdentifierLimit = 256;
    public string? Request { get; set; }
    public JsonElement? Schema { get; set; }
    public string? Output { get; set; }
    public bool OutputOmitted { get; set; }
    public GenerationMetadata? Metadata { get; set; }
    public AiCallUsage? Usage { get; set; }

    internal void Capture(ChatResponse response, GenerationMetadata metadata, AiCallUsage usage)
    {
        Output = response.Text.Length <= AiGenerationOptions.OutputCharacterLimit ? response.Text : null;
        OutputOmitted = Output is null;
        Metadata = metadata;
        Usage = usage;
    }
}

/// <summary>Known response facts survive artifact expiration. Null usage/cost is unknown, never zero by inference.</summary>
/// <remarks>Cache reads are included in input tokens.</remarks>
public sealed record AiCallUsage(string? Model, string? ResponseId, string? FinishReason,
    long? InputTokens, long? OutputTokens, long? ReasoningTokens, decimal? CostCredits,
    long? CacheReadTokens = null, long? CacheWriteTokens = null)
{
    /// <summary>Key for cache writes in shared chat usage.</summary>
    public const string CacheWriteTokensKey = "CacheWriteTokens";

    /// <summary>Captures bounded usage for evidence, logs and evaluation.</summary>
    public static AiCallUsage FromResponse(ChatResponse response) => new(
        Bounded(response.ModelId), Bounded(response.ResponseId), Bounded(response.FinishReason?.Value),
        response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, response.Usage?.ReasoningTokenCount,
        response.AdditionalProperties?.TryGetValue("costCredits", out var cost) == true && cost is decimal value && value >= 0 ? value : null,
        response.Usage?.CachedInputTokenCount is { } read && read >= 0 ? read : null,
        response.Usage?.AdditionalCounts?.TryGetValue(CacheWriteTokensKey, out var written) == true && written >= 0 ? written : null);

    // Optional provider identifiers must not turn the permanent usage record into unbounded raw evidence.
    private static string? Bounded(string? value) => value?.Length <= AiCallEvidence.IdentifierLimit ? value : null;
}
