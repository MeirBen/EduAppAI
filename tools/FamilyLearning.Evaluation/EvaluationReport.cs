using System.Text.Json;
using System.Text.Json.Serialization;
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
    public const int CurrentFormatVersion = 2;
    [JsonRequired] public int FormatVersion { get; init; } = CurrentFormatVersion;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    [JsonRequired] public string Status { get; set; } = "running";
    public string SuiteSha256 { get; } = suiteSha256;
    /// <summary>Optional developer description, at most 120 characters; excluded from comparison semantics.</summary>
    public string? Label { get; init; }
    /// <summary>Optional developer context, at most 4,000 characters; displayed only as plain text.</summary>
    public string? RunNotes { get; init; }
    public Dictionary<string, string?> Profile { get; } = profile;
    public EvaluationCase[] Cases { get; } = cases;
    public int Repeat { get; } = repeat;
    // These fields are required when judging is enabled, checked by the report reader.
    public string CalibrationSha256 { get; init; } = "";
    public CalibrationSample[] CalibrationSamples { get; init; } = [];
    public string JudgePromptVersion { get; init; } = "";
    public string JudgePrompt { get; init; } = "";
    [JsonRequired] public bool JudgeEnabled { get; init; }
    public int MaxCalls { get; init; } = 100;
    public int PlannedCalls => CountCalls(Cases.Length, Repeat, JudgeEnabled ? CalibrationSamples.Length : 0);
    public List<CalibrationResult> Calibration { get; init; } = [];
    [JsonRequired] public List<EvaluationResult> Results { get; init; } = [];
    public int AttemptedCalls => Steps.Count(step => step.RequestSent);
    public int AutomaticPasses => Results.Count(result => result.Generation?.ContractValid == true && result.Checks.Values.All(value => value));
    public int CallsWithReportedCost => Steps.Count(step => step.CostCredits.HasValue);
    public decimal? ReportedCostCredits => CallsWithReportedCost == 0 ? null : Steps.Sum(step => step.CostCredits ?? 0);
    /// <summary>Null means disabled or unfinished, never a pass. Content findings do not affect calibration.</summary>
    public bool? JudgeCalibrationPassed => !JudgeEnabled || Calibration.Count != CalibrationSamples.Length ||
        Calibration.Any(result => result.Call.FinishedAtUtc is null) ? null : Calibration.All(result => result.Passed);
    public int CalibrationFailureCount => Calibration.Count(result => result.Call.FinishedAtUtc is not null && !result.Passed);
    public int GeneratedHebrewIssueCount => GeneratedIssues.Count();
    public bool HasHebrewFindings => GeneratedHebrewIssueCount > 0;
    [JsonIgnore]
    public IEnumerable<HebrewIssue> GeneratedIssues => Results.Where(result => result.Judge?.ContractValid == true)
        .SelectMany(result => result.Issues ?? []);
    [JsonIgnore]
    public IEnumerable<EvaluationStep> Steps => Calibration.Select(result => result.Call).Concat(
        Results.SelectMany(result => new[] { result.Authoring, result.Generation, result.Judge }).OfType<EvaluationStep>());

    public static int CountCalls(int caseCount, int repeat, int calibrationCount) =>
        caseCount * repeat * (calibrationCount > 0 ? 3 : 2) + calibrationCount;
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
    [JsonRequired] public Dictionary<string, bool> Checks { get; init; } = [];
    [JsonRequired] public ManualReview Review { get; init; } = new();
}

/// <summary>Only final content and whitelisted diagnostics are retained, including rejected outputs.</summary>
public sealed class EvaluationStep
{
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    [JsonRequired] public DateTime? FinishedAtUtc { get; set; }
    [JsonRequired] public bool RequestSent { get; set; }
    [JsonRequired] public bool ResponseReceived { get; set; }
    [JsonRequired] public double ElapsedMilliseconds { get; set; }
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
    [JsonRequired] public bool ContractValid { get; set; }
    public int? StatusCode { get; set; }
    public string? Failure { get; set; }
}

public sealed record EvaluationMessage(string Role, string Text);

/// <summary>Fixed human-labelled controls measure known error detection and false alarms, not general judge accuracy.</summary>
public sealed record CalibrationSample(string Id, string Request, ReviewText[] Texts,
    ExpectedHebrewIssue[] ExpectedIssues, bool AllowUnexpectedFindings = false)
{
    // Record equality compares arrays by reference; reports need the captured fields and policy to match.
    internal bool HasSameContent(CalibrationSample? other) => other is not null &&
        Id == other.Id && Request == other.Request && AllowUnexpectedFindings == other.AllowUnexpectedFindings &&
        other.Texts is not null && Texts.SequenceEqual(other.Texts) &&
        other.ExpectedIssues is not null && ExpectedIssues.SequenceEqual(other.ExpectedIssues);
}
public sealed record ExpectedHebrewIssue(string Path, string Quote)
{
    /// <summary>Match whole tokens or a short containing phrase on the same field; never an unrelated path.</summary>
    public bool Matches(HebrewIssue issue) => Path == issue.Path && Math.Abs(Quote.Length - issue.Quote.Length) <= 60 &&
        (ContainsPhrase(Quote, issue.Quote) || ContainsPhrase(issue.Quote, Quote));

    private static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        for (var start = 0; start <= text.Length - phrase.Length; start++)
        {
            if (text.AsSpan(start).StartsWith(phrase, StringComparison.Ordinal) &&
                (start == 0 || !IsWordCharacter(text[start - 1])) &&
                (start + phrase.Length == text.Length || !IsWordCharacter(text[start + phrase.Length]))) return true;
        }
        return false;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) ||
        char.GetUnicodeCategory(value) == System.Globalization.UnicodeCategory.NonSpacingMark;
}
public sealed record CalibrationResult(CalibrationSample Sample, EvaluationStep Call)
{
    public HebrewIssue[]? Issues { get; set; }
    public int MissingExpectedIssueCount => Sample.ExpectedIssues.Count(expected => Issues?.Any(expected.Matches) != true);
    public int UnexpectedFindingCount => Issues?.Count(issue => !Sample.ExpectedIssues.Any(expected => expected.Matches(issue))) ?? 0;
    public bool Passed => Call.ContractValid && Issues is not null && MissingExpectedIssueCount == 0 &&
        (Sample.AllowUnexpectedFindings || UnexpectedFindingCount == 0);
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

    /// <summary>Rejects scores outside the rubric and notes longer than 4,000 characters for files and UI edits.</summary>
    public void Validate()
    {
        if (Scores().Values.Any(score => score is < 0 or > 2) || Notes?.Length > 4000)
            throw new InvalidDataException("Human review requires scores of 0, 1, 2 or null and notes of at most 4,000 characters.");
    }

    /// <summary>Stable rubric keys shared by score validation and comparison; null remains unreviewed.</summary>
    public Dictionary<string, int?> Scores() => new()
    {
        ["hebrew"] = Hebrew,
        ["correctness"] = Correctness,
        ["ageFit"] = AgeFit,
        ["adherence"] = Adherence,
        ["answerClarity"] = AnswerClarity,
        ["consistency"] = Consistency
    };
}
