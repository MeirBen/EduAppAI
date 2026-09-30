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
    /// <summary>Reads existing reports only. Ignores cached summary.json so edited human scores are reflected.</summary>
    public static async Task<EvaluationComparison> ReadAsync(string baselinePath, string candidatePath) =>
        Compare(await EvaluationFiles.ReadReportAsync(baselinePath), await EvaluationFiles.ReadReportAsync(candidatePath));

    public static EvaluationComparison Compare(EvaluationReport baseline, EvaluationReport candidate)
    {
        var before = EvaluationSummary.Create(baseline);
        var after = EvaluationSummary.Create(candidate);
        var incompatible = new List<string>();
        if (baseline.Prototype || candidate.Prototype) incompatible.Add("prototype-requires-matched-variant-review");
        if (baseline.SuiteSha256 != candidate.SuiteSha256) incompatible.Add("case-suite-hash");
        if (!baseline.Cases.Select(item => item.Id).SequenceEqual(candidate.Cases.Select(item => item.Id))) incompatible.Add("selected-cases-or-order");
        // Compare the captured inputs too: a stale/manually edited hash cannot make different prompts comparable.
        if (!baseline.Cases.SequenceEqual(candidate.Cases)) incompatible.Add("scenario-inputs");
        if (baseline.Repeat != candidate.Repeat) incompatible.Add("repeat-count");
        if (baseline.AutomaticChecksVersion != candidate.AutomaticChecksVersion) incompatible.Add("automatic-checks-version");
        if (!IsComplete(baseline) || !IsComplete(candidate)) incompatible.Add("incomplete-run");
        if (baseline.JudgeEnabled != candidate.JudgeEnabled) incompatible.Add("judge-mode");
        if (baseline.JudgeEnabled && candidate.JudgeEnabled)
        {
            if (baseline.CalibrationSha256 != candidate.CalibrationSha256) incompatible.Add("calibration-suite-hash");
            if (baseline.CalibrationSamples.Length != candidate.CalibrationSamples.Length ||
                baseline.CalibrationSamples.Zip(candidate.CalibrationSamples).Any(pair => !pair.First.HasSameContent(pair.Second)))
                incompatible.Add("calibration-inputs");
            if (baseline.JudgePromptVersion != candidate.JudgePromptVersion || baseline.JudgePrompt != candidate.JudgePrompt)
                incompatible.Add("judge-prompt");
        }
        var profiles = baseline.Profile.Keys.Union(candidate.Profile.Keys).Order(StringComparer.Ordinal)
            .Where(key => baseline.Profile.GetValueOrDefault(key) != candidate.Profile.GetValueOrDefault(key))
            .ToDictionary(key => key, key => new ProfileChange(baseline.Profile.GetValueOrDefault(key), candidate.Profile.GetValueOrDefault(key)));
        var deltas = new Dictionary<string, decimal?>
        {
            ["attemptedCalls"] = after.AttemptedCalls - before.AttemptedCalls,
            ["authoringSuccesses"] = after.Authoring.Succeeded - before.Authoring.Succeeded,
            ["authoringFailures"] = after.Authoring.Failed - before.Authoring.Failed,
            ["generationSuccesses"] = after.Generation.Succeeded - before.Generation.Succeeded,
            ["generationFailures"] = after.Generation.Failed - before.Generation.Failed,
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
        var hebrewComparable = incompatible.Count == 0 && before.JudgeCalibrationPassed == true && after.JudgeCalibrationPassed == true &&
            before.GeneratedContentReviews.Succeeded > 0 && ReviewKeys(baseline).SequenceEqual(ReviewKeys(candidate));
        if (!hebrewComparable) deltas["generatedHebrewIssues"] = null;
        return new(incompatible.Count == 0, incompatible.ToArray(), hebrewComparable, profiles,
            baseline.JudgePromptVersion, candidate.JudgePromptVersion, before, after, deltas,
            CountDeltas(before.AutomaticFailuresByCheck, after.AutomaticFailuresByCheck),
            hebrewComparable ? CountDeltas(before.HebrewIssuesByKind, after.HebrewIssuesByKind) : null, humanComparable);
    }

    private static bool IsComplete(EvaluationReport report)
    {
        if (report.Status != "completed" || report.FinishedAtUtc is null || report.Results.Count != report.Cases.Length * report.Repeat ||
            report.Steps.Any(step => !step.RequestSent || step.FinishedAtUtc is null) ||
            (report.JudgeEnabled && report.Calibration.Count != report.CalibrationSamples.Length)) return false;
        // Completed failures count as evidence; absent follow-up calls are valid only when an earlier stage prevented them.
        return report.Results.All(result => result.Authoring is not null &&
            (!result.Authoring.ContractValid || result.Checks.GetValueOrDefault("parameterDefaults", true) == false || result.Generation is not null) &&
            (!report.JudgeEnabled || result.Generation?.ContractValid != true || result.Judge is not null));
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
}
