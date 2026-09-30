namespace FamilyLearning.Evaluation;

/// <summary>A known subtotal with explicit coverage. Null means no measurement, never free or zero use.</summary>
public sealed record ReportedTotal(decimal? KnownTotal, int KnownCalls, int MissingCalls)
{
    internal static ReportedTotal From(EvaluationStep[] calls, Func<EvaluationStep, decimal?> select)
    {
        var known = calls.Select(select).OfType<decimal>().ToArray();
        return new(known.Length == 0 ? null : known.Sum(), known.Length, calls.Length - known.Length);
    }
}

public sealed record StageCounts(int Attempted, int Succeeded, int Failed, int NotAttempted)
{
    internal static StageCounts From(IEnumerable<EvaluationStep?> steps, int planned)
    {
        var attempted = steps.OfType<EvaluationStep>().Where(step => step.RequestSent).ToArray();
        return new(attempted.Length, attempted.Count(step => step.ContractValid),
            attempted.Count(step => step.FinishedAtUtc.HasValue && !step.ContractValid), planned - attempted.Length);
    }
}

public sealed record HumanScoreSummary(int Reviewed, int Unreviewed, double? Average);

/// <summary>Deterministic aggregation of run evidence. Calibration, content findings and human scores stay separate.</summary>
public sealed record EvaluationSummary
{
    public required string Status { get; init; }
    public int CaseCount { get; init; }
    public int Repeat { get; init; }
    public int PlannedCaseRuns { get; init; }
    public int RecordedCaseRuns { get; init; }
    public int AttemptedCalls { get; init; }
    public required StageCounts Authoring { get; init; }
    public required StageCounts Generation { get; init; }
    public required StageCounts GeneratedContentReviews { get; init; }
    public int ScenarioAutomaticPasses { get; init; }
    public required Dictionary<string, int> AutomaticFailuresByCheck { get; init; }
    public bool JudgeEnabled { get; init; }
    public bool? JudgeCalibrationPassed { get; init; }
    public int CalibrationFailureCount { get; init; }
    public int CalibrationCompletedCount { get; init; }
    public int CalibrationPlannedCount { get; init; }
    public int GeneratedHebrewIssueCount { get; init; }
    public required Dictionary<string, int> HebrewIssuesByKind { get; init; }
    public required string[] CasesWithHebrewFindings { get; init; }
    public required ReportedTotal InputTokens { get; init; }
    public required ReportedTotal OutputTokens { get; init; }
    public required ReportedTotal ReasoningTokens { get; init; }
    public required ReportedTotal CostCredits { get; init; }
    public int CallsWithResponse { get; init; }
    public double? AverageLatencyMilliseconds { get; init; }
    public required string[] ActualModels { get; init; }
    public required string[] GenerationPromptVersions { get; init; }
    public required Dictionary<string, HumanScoreSummary> HumanReview { get; init; }

    public static EvaluationSummary Create(EvaluationReport report)
    {
        var planned = report.Cases.Length * report.Repeat * (report.Prototype ? 2 : 1);
        var calls = report.Steps.Where(step => step.RequestSent).ToArray();
        var responses = calls.Where(step => step.ResponseReceived && step.FinishedAtUtc.HasValue).ToArray();
        var prototypeSteps = report.PrototypeResults.SelectMany(result => result.Stages).Select(stage => stage.Call).OfType<EvaluationStep>();
        var generationSteps = report.Results.SelectMany(result => new[] { result.Authoring, result.Generation }).OfType<EvaluationStep>().Concat(prototypeSteps);
        var reviews = report.Results.Select(result => result.Review.Scores()).Concat(report.PrototypeResults.Select(result => result.Review.Scores())).ToArray();
        var humanScores = new Dictionary<string, HumanScoreSummary>();
        foreach (var key in new ManualReview().Scores().Keys)
        {
            var scores = reviews.Select(review => review[key]).OfType<int>().ToArray();
            humanScores[key] = new(scores.Length, planned - scores.Length, scores.Length == 0 ? null : scores.Average());
        }
        return new()
        {
            Status = report.Status,
            CaseCount = report.Cases.Length,
            Repeat = report.Repeat,
            PlannedCaseRuns = planned,
            RecordedCaseRuns = report.Results.Count + report.PrototypeResults.Count,
            AttemptedCalls = calls.Length,
            Authoring = StageCounts.From(report.Results.Select(result => result.Authoring), report.Prototype ? 0 : planned),
            Generation = report.Prototype ? StageCounts.From(prototypeSteps, report.PlannedCalls) : StageCounts.From(report.Results.Select(result => result.Generation), planned),
            GeneratedContentReviews = StageCounts.From(report.Results.Select(result => result.Judge), report.JudgeEnabled ? planned : 0),
            ScenarioAutomaticPasses = report.AutomaticPasses,
            AutomaticFailuresByCheck = Count(report.Results.SelectMany(result => result.Checks).Where(check => !check.Value).Select(check => check.Key)),
            JudgeEnabled = report.JudgeEnabled,
            JudgeCalibrationPassed = report.JudgeCalibrationPassed,
            CalibrationFailureCount = report.CalibrationFailureCount,
            CalibrationCompletedCount = report.Calibration.Count(result => result.Call.FinishedAtUtc.HasValue),
            CalibrationPlannedCount = report.JudgeEnabled ? report.CalibrationSamples.Length : 0,
            GeneratedHebrewIssueCount = report.GeneratedHebrewIssueCount,
            HebrewIssuesByKind = Count(report.GeneratedIssues.Select(issue => issue.Kind)),
            CasesWithHebrewFindings = report.Results.Where(result => result.Judge?.ContractValid == true && result.Issues is { Length: > 0 })
                .Select(result => result.CaseId).Distinct().Order(StringComparer.Ordinal).ToArray(),
            InputTokens = ReportedTotal.From(calls, step => step.InputTokens),
            OutputTokens = ReportedTotal.From(calls, step => step.OutputTokens),
            ReasoningTokens = ReportedTotal.From(calls, step => step.ReasoningTokens),
            CostCredits = ReportedTotal.From(calls, step => step.CostCredits),
            CallsWithResponse = responses.Length,
            AverageLatencyMilliseconds = responses.Length == 0 ? null : responses.Average(step => step.ElapsedMilliseconds),
            ActualModels = calls.Select(step => step.Model).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToArray(),
            GenerationPromptVersions = generationSteps.Select(step => step.Metadata?.PromptVersion ?? step.SchemaName?.Replace('_', '-'))
                .OfType<string>().Distinct().Order(StringComparer.Ordinal).ToArray(),
            HumanReview = humanScores
        };
    }

    private static Dictionary<string, int> Count(IEnumerable<string> values) => values.GroupBy(value => value, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count());
}
