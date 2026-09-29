using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;

namespace FamilyLearning.Evaluation;

/// <summary>Synthetic parent request, measurable expectations and a case-specific human review focus.</summary>
public sealed record EvaluationCase(string Id, string Prompt, string ReviewFocus, int QuestionCount,
    string Interaction, int? ChoiceCount, int? MinPassageWords, int? MaxPassageWords,
    bool UseMaximumQuestionCount = false);

/// <summary>Local evaluation artifact. Contract success never implies educational or language quality.</summary>
public sealed class EvaluationReport(EvaluationCase[] cases, int repeat, string suiteSha256,
    Dictionary<string, string?> profile)
{
    public int FormatVersion => 1;
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public string Status { get; set; } = "running";
    public string SuiteSha256 { get; } = suiteSha256;
    public Dictionary<string, string?> Profile { get; } = profile;
    public EvaluationCase[] Cases { get; } = cases;
    public int Repeat { get; } = repeat;
    public bool JudgeEnabled { get; init; }
    public int MaxCalls { get; init; } = 100;
    public int PlannedCalls => Cases.Length * Repeat * (JudgeEnabled ? 3 : 2) + (JudgeEnabled ? 2 : 0);
    public List<CalibrationResult> Calibration { get; } = [];
    public List<EvaluationResult> Results { get; } = [];
    public int AttemptedCalls => Steps.Count(step => step.RequestSent);
    public int AutomaticPasses => Results.Count(result => result.Generation?.ContractValid == true && result.Checks.Values.All(value => value));
    public int CallsWithReportedCost => Steps.Count(step => step.CostCredits.HasValue);
    public decimal ReportedCostCredits => Steps.Sum(step => step.CostCredits ?? 0);
    public bool JudgeChecksPassed => !JudgeEnabled || Calibration.Count == 2 && Calibration.All(result => result.Passed) &&
        Results.All(result => result.Judge?.ContractValid == true && result.Issues is { Length: 0 });
    private IEnumerable<EvaluationStep> Steps => Calibration.Select(result => result.Call).Concat(
        Results.SelectMany(result => new[] { result.Authoring, result.Generation, result.Judge }).OfType<EvaluationStep>());
}

/// <summary>One independent authoring-to-instance run; generation is absent if authoring fails.</summary>
public sealed class EvaluationResult(string caseId, int repetition)
{
    public string CaseId { get; } = caseId;
    public int Repetition { get; } = repetition;
    public EvaluationStep? Authoring { get; set; }
    public EvaluationStep? Generation { get; set; }
    public EvaluationStep? Judge { get; set; }
    public HebrewIssue[]? Issues { get; set; }
    public Dictionary<string, JsonElement>? Parameters { get; set; }
    public Dictionary<string, bool> Checks { get; } = [];
    public ManualReview Review { get; } = new();
}

/// <summary>Only final content and whitelisted diagnostics are retained, including rejected outputs.</summary>
public sealed class EvaluationStep
{
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public bool RequestSent { get; set; }
    public double ElapsedMilliseconds { get; set; }
    public EvaluationMessage[] Request { get; set; } = [];
    public string? SchemaName { get; set; }
    public string? Output { get; set; }
    public string? Model { get; set; }
    public string? ResponseId { get; set; }
    public string? FinishReason { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public decimal? CostCredits { get; set; }
    public GenerationMetadata? Metadata { get; set; }
    public bool ContractValid { get; set; }
    public int? StatusCode { get; set; }
    public string? Failure { get; set; }
}

public sealed record EvaluationMessage(string Role, string Text);

/// <summary>Fixed human-labelled controls measure known error detection and false alarms, not general judge accuracy.</summary>
public sealed record CalibrationSample(string Id, string Request, ReviewText[] Texts, string[] ExpectedQuotes);
public sealed record CalibrationResult(CalibrationSample Sample, EvaluationStep Call)
{
    public HebrewIssue[]? Issues { get; set; }
    public bool Passed => Call.ContractValid && Issues is not null && (Sample.ExpectedQuotes.Length == 0
        ? Issues.Length == 0
        : Sample.ExpectedQuotes.All(expected => Issues.Any(issue => issue.Quote.Contains(expected, StringComparison.Ordinal))));
}

/// <summary>Reviewer fills 0 (unusable), 1 (needs edits), 2 (ready); null means not reviewed.</summary>
public sealed class ManualReview
{
    public int? Hebrew { get; set; }
    public int? Correctness { get; set; }
    public int? AgeFit { get; set; }
    public int? Adherence { get; set; }
    public int? AnswerClarity { get; set; }
    public int? Consistency { get; set; }
    public string? Notes { get; set; }
}
