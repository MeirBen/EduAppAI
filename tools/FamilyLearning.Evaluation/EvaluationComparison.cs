using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;

namespace FamilyLearning.Evaluation;

public sealed record ProfileChange(string? Baseline, string? Candidate);

/// <summary>Descriptive deltas, never a winner or combined score. Positive deltas mean candidate minus baseline.</summary>
public sealed record EvaluationComparison(
    bool DirectlyComparable, string[] Incompatibilities, bool HebrewFindingsComparable,
    Dictionary<string, ProfileChange> ProfileChanges, string BaselineJudgeVersion, string CandidateJudgeVersion,
    EvaluationSummary Baseline, EvaluationSummary Candidate, Dictionary<string, decimal?> Deltas,
    Dictionary<string, int> CheckFailureDeltas, Dictionary<string, int>? HebrewKindDeltas,
    Dictionary<string, bool> HumanReviewComparable)
{
    public bool GenerationComparable { get; init; }
    public string[] JudgeIncompatibilities { get; init; } = [];
    /// <summary>Reads existing reports only. Ignores cached summary.json so edited human scores are reflected.</summary>
    public static async Task<EvaluationComparison> ReadAsync(string baselinePath, string candidatePath) =>
        Compare(await EvaluationFiles.ReadReportAsync(baselinePath), await EvaluationFiles.ReadReportAsync(candidatePath));

    public static EvaluationComparison Compare(EvaluationReport baseline, EvaluationReport candidate)
    {
        var before = EvaluationSummary.Create(baseline);
        var after = EvaluationSummary.Create(candidate);
        var incompatible = new List<string>();
        var judgeIncompatible = new List<string>();
        if (baseline.FormatVersion != EvaluationVersions.ReportFormat || candidate.FormatVersion != EvaluationVersions.ReportFormat)
            incompatible.Add("report-format");
        if (baseline.Prototype || candidate.Prototype) incompatible.Add("prototype-requires-matched-variant-review");
        if (baseline.SuiteSha256 != candidate.SuiteSha256) incompatible.Add("case-suite-hash");
        if (!baseline.Cases.Select(item => item.Id).SequenceEqual(candidate.Cases.Select(item => item.Id))) incompatible.Add("selected-cases-or-order");
        // Compare the captured inputs too: a stale/manually edited hash cannot make different prompts comparable.
        if (!Same(baseline.Cases, candidate.Cases)) incompatible.Add("scenario-inputs");
        if (baseline.Repeat != candidate.Repeat) incompatible.Add("repeat-count");
        if (baseline.AutomaticChecksVersion != candidate.AutomaticChecksVersion) incompatible.Add("automatic-checks-version");
        if (!IsComplete(baseline) || !IsComplete(candidate)) incompatible.Add("incomplete-run");
        if (!baseline.JudgeEnabled || !candidate.JudgeEnabled) judgeIncompatible.Add("judge-mode");
        if (baseline.JudgeEnabled && candidate.JudgeEnabled)
        {
            if (baseline.CalibrationSha256 != candidate.CalibrationSha256) judgeIncompatible.Add("calibration-suite-hash");
            if (baseline.CalibrationSamples.Length != candidate.CalibrationSamples.Length ||
                baseline.CalibrationSamples.Zip(candidate.CalibrationSamples).Any(pair => !pair.First.HasSameContent(pair.Second)))
                judgeIncompatible.Add("calibration-inputs");
            if (baseline.JudgePromptVersion != candidate.JudgePromptVersion || baseline.JudgePrompt != candidate.JudgePrompt)
                judgeIncompatible.Add("judge-prompt");
        }
        var profiles = baseline.Profile.Keys.Union(candidate.Profile.Keys).Order(StringComparer.Ordinal)
            .Where(key => baseline.Profile.GetValueOrDefault(key) != candidate.Profile.GetValueOrDefault(key))
            .ToDictionary(key => key, key => new ProfileChange(baseline.Profile.GetValueOrDefault(key), candidate.Profile.GetValueOrDefault(key)));
        if (profiles.Count > 0) judgeIncompatible.Add("judge-profile");
        if (before.JudgeCalibrationPassed != true || after.JudgeCalibrationPassed != true) judgeIncompatible.Add("judge-calibration");
        if (before.GeneratedContentReviews.Succeeded == 0 || !ReviewKeys(baseline).SequenceEqual(ReviewKeys(candidate)))
            judgeIncompatible.Add("judge-coverage");
        if (baseline.Results.Concat(candidate.Results).Any(result => result.Judge?.ContractValid == true &&
            (string.IsNullOrWhiteSpace(result.Judge.Model) || result.Judge.Model == "unknown" ||
             string.IsNullOrWhiteSpace(result.Judge.Metadata?.Provider) || string.IsNullOrWhiteSpace(result.Judge.Metadata?.PromptVersion))) ||
            !JudgeIdentities(baseline).SequenceEqual(JudgeIdentities(candidate))) judgeIncompatible.Add("judge-identity");
        var generationComparable = incompatible.Count == 0 && baseline.Results.All(result => result.Input is not null) && candidate.Results.All(result => result.Input is not null) && GenerationInputs(baseline).SequenceEqual(GenerationInputs(candidate));
        if (!generationComparable && baseline.Cases.All(scenario => scenario.InitialPlan is not null)) incompatible.Add("generation-inputs");
        var deltas = new Dictionary<string, decimal?>
        {
            ["attemptedCalls"] = after.AttemptedCalls - before.AttemptedCalls,
            ["authoringSuccesses"] = after.Authoring.Succeeded - before.Authoring.Succeeded,
            ["authoringFailures"] = after.Authoring.Failed - before.Authoring.Failed,
            ["generationSuccesses"] = generationComparable ? after.Generation.Succeeded - before.Generation.Succeeded : null,
            ["generationFailures"] = generationComparable ? after.Generation.Failed - before.Generation.Failed : null,
            ["scenarioAutomaticPasses"] = after.ScenarioAutomaticPasses - before.ScenarioAutomaticPasses,
            ["calibrationFailures"] = after.CalibrationFailureCount - before.CalibrationFailureCount,
            ["generatedHebrewIssues"] = after.GeneratedHebrewIssueCount - before.GeneratedHebrewIssueCount,
            ["averageLatencyMilliseconds"] = (decimal?)after.AverageLatencyMilliseconds - (decimal?)before.AverageLatencyMilliseconds,
            ["inputTokens"] = CompleteDelta(before.InputTokens, after.InputTokens),
            ["outputTokens"] = CompleteDelta(before.OutputTokens, after.OutputTokens),
            ["reasoningTokens"] = CompleteDelta(before.ReasoningTokens, after.ReasoningTokens),
            ["costCredits"] = CompleteDelta(before.CostCredits, after.CostCredits)
        };
        var humanComparable = new Dictionary<string, bool>();
        foreach (var key in before.HumanReview.Keys)
        {
            var sameReviewed = ReviewedKeys(baseline, key).SequenceEqual(ReviewedKeys(candidate, key));
            humanComparable[key] = incompatible.Count == 0 && sameReviewed && before.HumanReview[key].Reviewed > 0;
            deltas[$"humanReview.{key}.average"] = humanComparable[key]
                ? (decimal?)after.HumanReview[key].Average - (decimal?)before.HumanReview[key].Average : null;
        }
        // Fewer findings on fewer reviewed outputs is not an improvement in Hebrew quality.
        var hebrewComparable = incompatible.Count == 0 && judgeIncompatible.Count == 0;
        if (!hebrewComparable)
        {
            deltas["generatedHebrewIssues"] = null;
            deltas["calibrationFailures"] = null;
        }
        // These totals include calibration/reviews, so a changed judge workload cannot measure a workflow delta.
        if ((baseline.JudgeEnabled || candidate.JudgeEnabled) && !hebrewComparable)
            foreach (var key in new[] { "attemptedCalls", "averageLatencyMilliseconds", "inputTokens", "outputTokens", "reasoningTokens", "costCredits" })
                deltas[key] = null;
        if (incompatible.Count > 0)
            foreach (var key in deltas.Keys) deltas[key] = null;
        return new(incompatible.Count == 0, incompatible.ToArray(), hebrewComparable, profiles,
            baseline.JudgePromptVersion, candidate.JudgePromptVersion, before, after, deltas,
            incompatible.Count == 0 ? CountDeltas(before.AutomaticFailuresByCheck, after.AutomaticFailuresByCheck) : [],
            hebrewComparable ? CountDeltas(before.HebrewIssuesByKind, after.HebrewIssuesByKind) : null, humanComparable)
        { GenerationComparable = generationComparable, JudgeIncompatibilities = judgeIncompatible.ToArray() };
    }

    private static bool IsComplete(EvaluationReport report)
    {
        if (report.Status != "completed" || report.FinishedAtUtc is null || report.Results.Count != report.Cases.Length * report.Repeat ||
            report.Results.SelectMany(result => result.Steps.Where(step => step != result.Judge)).Any(step => step.Outcome != "skipped" && step.FinishedAtUtc is null)) return false;
        return report.Results.All(result => EvaluationFiles.HasCompleteWorkflow(report.Cases.Single(item => item.Id == result.CaseId), result));
    }

    private static decimal? CompleteDelta(ReportedTotal before, ReportedTotal after) =>
        before.MissingCalls == 0 && after.MissingCalls == 0 ? after.KnownTotal - before.KnownTotal : null;

    private static Dictionary<string, int> CountDeltas(Dictionary<string, int> before, Dictionary<string, int> after) =>
        before.Keys.Union(after.Keys).Order(StringComparer.Ordinal)
            .ToDictionary(key => key, key => after.GetValueOrDefault(key) - before.GetValueOrDefault(key));

    private static IEnumerable<(string, int)> ReviewedKeys(EvaluationReport report, string key) => report.Results
        .Where(result => result.Review.Scores()[key].HasValue).Select(result => (result.CaseId, result.Repetition)).Order();

    private static IEnumerable<(string, int)> ReviewKeys(EvaluationReport report) => report.Results
        .Where(result => result.Judge?.ContractValid == true).Select(result => (result.CaseId, result.Repetition)).Order();

    private static bool Same<T>(T before, T after) => JsonNode.DeepEquals(JsonSerializer.SerializeToNode(before), JsonSerializer.SerializeToNode(after));

    private static IEnumerable<(string, int, string?)> GenerationInputs(EvaluationReport report) => report.Results
        .OrderBy(result => result.CaseId, StringComparer.Ordinal).ThenBy(result => result.Repetition)
        .Select(result => (result.CaseId, result.Repetition, result.Input is null ? null : TaskRequestResolver.Fingerprint(result.Input)));

    private static IEnumerable<(string, int, string?, string?, string?)> JudgeIdentities(EvaluationReport report) => report.Results
        .Where(result => result.Judge?.ContractValid == true).OrderBy(result => result.CaseId, StringComparer.Ordinal).ThenBy(result => result.Repetition)
        .Select(result => (result.CaseId, result.Repetition, result.Judge!.Model, result.Judge.Metadata?.Provider, result.Judge.Metadata?.PromptVersion));
}
