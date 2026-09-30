using System.Text;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Evaluation;

/// <summary>Pre-registered comparison and explicit paid-call budget, loaded before resolving a live provider.</summary>
/// <remarks>Prices are conservative per-million-token ceilings checked against current provider pricing before the experiment.
/// Use a profile without fallback. Human review and held-out confirmation remain required for a cutover decision.</remarks>
public sealed record PrototypeExperiment(string Model, int MaxCalls, decimal MaxCostUsd,
    decimal InputUsdPerMillion, decimal OutputUsdPerMillion, string SampleDesign, string HeldOutCases,
    string UsableTaskRubric, string BlindReview, string AcceptableCostLatency, string ControlRecoveryGains)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model) || MaxCalls is < 1 or > 100 || MaxCostUsd <= 0 || InputUsdPerMillion < 0 || OutputUsdPerMillion <= 0 ||
            new[] { SampleDesign, HeldOutCases, UsableTaskRubric, BlindReview, AcceptableCostLatency, ControlRecoveryGains }
                .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 4000))
            throw new InvalidDataException("A prototype trial requires a bounded, complete pre-registration and cost budget.");
    }

    internal decimal Reserve(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        // UTF-8 bytes bound text tokens conservatively. Include schema overhead and message framing;
        // the output cap includes reasoning. Unknown usage keeps the full reservation.
        long bytes = 4096;
        foreach (var message in messages) bytes += Encoding.UTF8.GetByteCount(message.Text);
        if (options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema }) bytes += Encoding.UTF8.GetByteCount(schema.GetRawText());
        return (bytes * InputUsdPerMillion + (options?.MaxOutputTokens ?? throw new InvalidOperationException("Output cap required.")) * OutputUsdPerMillion) / 1_000_000m;
    }

    internal static async Task WriteBlindReviewAsync(EvaluationReport report, string directory)
    {
        var results = report.PrototypeResults.ToArray();
        Random.Shared.Shuffle(results);
        var entries = results.Select((result, index) => new
        {
            reviewId = $"R{index + 1:000}",
            result.Input.Goal,
            result.Input.Settings,
            reviewFocus = report.Cases.Single(c => c.Id == result.CaseId).ReviewFocus,
            requirements = new
            {
                result.Input.Guidance,
                materials = result.Input.Materials.Select(m => new
                {
                    m.Label,
                    m.Source,
                    m.Guidance,
                    m.Text,
                    m.Length,
                    controls = ReviewControls(m.Controls)
                }),
                questions = new
                {
                    result.Input.Questions.Formats,
                    result.Input.Questions.ChoiceCount,
                    result.Input.Questions.Guidance,
                    controls = ReviewControls(result.Input.Questions.Controls)
                },
                controls = ReviewControls(result.Input.Controls),
                result.Input.TotalLength
            },
            document = new
            {
                result.Document.Title,
                result.Document.Instructions,
                materials = result.Document.Materials.Select(m => new { m.Title, m.Body }),
                questions = result.Document.Questions.Select(q => new { q.Prompt, q.Interaction, q.Answer, q.Points })
            },
            scores = new ManualReview(),
            correctionNotes = "",
            correctionMinutes = (int?)null
        }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(directory, "blind-review.json"), JsonSerializer.Serialize(entries, EvaluationFiles.Json));
        var key = results.Select((result, index) => new { reviewId = $"R{index + 1:000}", result.CaseId, result.Repetition, result.Variant });
        await File.WriteAllTextAsync(Path.Combine(directory, "blind-key.json"), JsonSerializer.Serialize(key, EvaluationFiles.Json));
    }

    private static object ReviewControls(ResolvedControl[] controls) => controls.Select(control => new
    {
        control.Label,
        control.Type,
        control.Meaning,
        control.Unit,
        control.Value,
        control.OptionMeaning
    }).ToArray();
}

internal sealed class EvaluationCostLimitException : Exception;
