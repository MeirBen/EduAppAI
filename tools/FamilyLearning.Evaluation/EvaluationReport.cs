using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Evaluation;

/// <summary>Synthetic parent request, measurable expectations and a case-specific human review focus.</summary>
public sealed record EvaluationCase(string Id, string Prompt, string ReviewFocus, int QuestionCount,
    string Interaction, int? ChoiceCount, int? MinPassageWords, int? MaxPassageWords,
    LearningPlan? InitialPlan = null)
{
    public string[] Refinements { get; init; } = [];
    public EvaluationReplacement[] Replacements { get; init; } = [];
    /// <summary>Independent authoring expectation and preview budget; fixed plans derive this count from their materials.</summary>
    public int ExpectedGeneratedMaterials { get; init; }
    public ResolvedLength? ExpectedLength { get; init; }
    public int PlannedCalls => (InitialPlan is null ? 1 + Refinements.Length : 0) +
        ((InitialPlan?.Materials.Count(material => material.Source == "generated") ?? ExpectedGeneratedMaterials) > 0 ? 3 : 0) +
        1 + Replacements.Length;
}

/// <summary>One explicit scoped repair after generation. The runner selects an application-owned ID by zero-based index.</summary>
public sealed record EvaluationReplacement(string Stage, int TargetIndex, string? Instruction = null);

/// <summary>Local evaluation artifact. Contract success never implies educational or language quality.</summary>
public sealed class EvaluationReport(EvaluationCase[] cases, int repeat, string suiteSha256,
    Dictionary<string, string?> profile)
{
    [JsonRequired] public int FormatVersion { get; init; } = EvaluationVersions.ReportFormat;
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    [JsonRequired] public string Status { get; set; } = "running";
    /// <summary>Recorded deterministic-check version; required for meaningful comparison.</summary>
    [JsonRequired] public int AutomaticChecksVersion { get; init; } = EvaluationVersions.AutomaticChecks;
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
    /// <summary>The pinned judge's nonsecret profile; independent of the generation profile.</summary>
    public Dictionary<string, string?> JudgeProfile { get; init; } = [];
    [JsonRequired] public bool JudgeEnabled { get; init; }
    public int MaxCalls { get; init; } = 100;
    /// <summary>Recorded pause before each call after the first, outside request timing.</summary>
    [JsonRequired] public int CallDelaySeconds { get; init; }
    public int PlannedCalls => Cases.Sum(scenario => scenario.PlannedCalls + (JudgeEnabled ? 1 : 0)) * Repeat + (JudgeEnabled ? CalibrationSamples.Length : 0);
    [JsonRequired] public List<CalibrationResult> Calibration { get; init; } = [];
    [JsonRequired] public List<EvaluationResult> Results { get; init; } = [];
    /// <summary>Superseded 429 attempts; each stage retains its current or final attempt separately.</summary>
    public List<EvaluationRetry> Retries { get; init; } = [];
    public int AttemptedCalls => Steps.Count(step => step.RequestSent);
    public int AutomaticPasses => Results.Count(result => result.EndToEndReady);
    public int CallsWithReportedCost => Steps.Count(step => step.CostCredits.HasValue);
    public decimal? ReportedCostCredits => CallsWithReportedCost == 0 ? null : Steps.Sum(step => step.CostCredits ?? 0);
    /// <summary>Null means disabled or unfinished, never a pass. Content findings and advisory controls do not affect it.</summary>
    public bool? JudgeCalibrationPassed => !JudgeEnabled || Calibration.Count != CalibrationSamples.Length ||
        Calibration.Any(result => result.Call.FinishedAtUtc is null) ? null : Calibration.All(result => result.Sample.Advisory || result.Passed);
    public int CalibrationFailureCount => Calibration.Count(result => !result.Sample.Advisory && result.Call.FinishedAtUtc is not null && !result.Passed);
    public int GeneratedHebrewIssueCount => GeneratedIssues.Count();
    public bool HasHebrewFindings => GeneratedHebrewIssueCount > 0;
    [JsonIgnore]
    public IEnumerable<HebrewIssue> GeneratedIssues => Results.Where(result => result.Judge?.ContractValid == true)
        .SelectMany(result => result.Issues ?? []);
    [JsonIgnore]
    public IEnumerable<EvaluationStep> Steps => Calibration.Select(result => result.Call).Concat(
        Results.SelectMany(result => result.Steps))
        .Concat(Retries.Select(retry => retry.Call));

}

/// <summary>One trial retaining explicit stage skips, accepted plans/documents and independent outcomes.</summary>
public sealed class EvaluationResult(string caseId, int repetition)
{
    public string CaseId { get; } = caseId;
    public int Repetition { get; } = repetition;
    public EvaluationStep? Authoring { get; set; }
    [JsonRequired] public List<EvaluationStep> Refinements { get; init; } = [];
    public EvaluationStep? MaterialIdeas { get; set; }
    /// <summary>The validated selected idea, kept even when writing fails.</summary>
    public MaterialIdea? SelectedMaterialIdea { get; set; }
    public EvaluationStep? Materials { get; set; }
    /// <summary>The applied polish of the written text; the writing step keeps the text as first written.</summary>
    public EvaluationStep? MaterialPolish { get; set; }
    public EvaluationStep? Generation { get; set; }
    [JsonRequired] public List<EvaluationStep> Replacements { get; init; } = [];
    public EvaluationStep? Judge { get; set; }
    public HebrewIssue[]? Issues { get; set; }
    public LearningPlan? Plan { get; set; }
    public ResolvedTaskRequest? Input { get; set; }
    public TaskDocument? Document { get; set; }
    public LengthMeasurement[] Measurements { get; set; } = [];
    public bool? InterpretationPassed { get; set; }
    public bool GenerationPassed { get; set; }
    public bool? ReplacementPassed { get; set; }
    /// <summary>Deterministic readiness and independent case adherence, never a substitute for parent review.</summary>
    public bool EndToEndReady { get; set; }
    [JsonIgnore]
    public IEnumerable<EvaluationStep> Steps => new[] { Authoring }.OfType<EvaluationStep>()
        .Concat(Refinements).Concat(new[] { MaterialIdeas, Materials, MaterialPolish, Generation }.OfType<EvaluationStep>())
        .Concat(Replacements).Concat(new[] { Judge }.OfType<EvaluationStep>());
    /// <summary>Shared TextLength count across material bodies; null when no independent length check ran.</summary>
    public int? PassageWordCount { get; set; }
    /// <summary>One-based position shared by at least three choice answers. Advisory only; ordered options may be intentional.</summary>
    public int? RepeatedAnswerPosition { get; set; }
    /// <summary>Bare calculation prompts and their recalculated keys; null when no prompt is a bare calculation.</summary>
    public CalculationCoverage? Calculations { get; set; }
    [JsonRequired] public Dictionary<string, bool> Checks { get; init; } = [];
    [JsonRequired] public ManualReview Review { get; set; } = new();
}

/// <summary>Captured request, final output and safe diagnostics; excludes separate reasoning text and raw provider errors.</summary>
public sealed class EvaluationStep
{
    [JsonRequired] public string Role { get; set; } = "authoring";
    [JsonRequired] public int EngineRevision { get; init; } = EngineVersions.Revision;
    [JsonRequired] public int SchemaVersion { get; init; } = EngineVersions.SchemaVersion;
    [JsonRequired] public string Outcome { get; set; } = "pending";
    public string? SkipReason { get; set; }
    /// <summary>Parsed provider output remains untrusted until Applied; malformed output is retained in Output.</summary>
    public JsonElement? Candidate { get; set; }
    public bool Applied { get; set; }
    public JsonElement? EffectiveInput { get; set; }
    public string? InputFingerprint { get; set; }
    public MaterialRevision[] Sources { get; set; } = [];
    public string? TargetId { get; set; }
    public string? Provider => Metadata?.Provider;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    [JsonRequired] public DateTime? FinishedAtUtc { get; set; }
    [JsonRequired] public bool RequestSent { get; set; }
    [JsonRequired] public bool ResponseReceived { get; set; }
    [JsonRequired] public double ElapsedMilliseconds { get; set; }
    public EvaluationMessage[] Request { get; set; } = [];
    public string? SchemaName { get; set; }
    public JsonElement? Schema { get; set; }
    public string? RequestSha256 { get; set; }
    public string? SchemaSha256 { get; set; }
    public string? Output { get; set; }
    public string? Model { get; set; }
    public string? ResponseId { get; set; }
    public string? FinishReason { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public long? CacheReadTokens { get; set; }
    public long? CacheWriteTokens { get; set; }
    public decimal? CostCredits { get; set; }
    public GenerationMetadata? Metadata { get; set; }
    [JsonRequired] public bool ContractValid { get; set; }
    public int? StatusCode { get; set; }
    /// <summary>Sanitized Retry-After seconds, when supplied by the provider; no raw headers are retained.</summary>
    public double? RetryAfterSeconds { get; set; }
    public string? Failure { get; set; }
    /// <summary>Safe field errors from the application validator; null for other failures.</summary>
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; set; }
}

/// <summary>Keys this check could not read (applicable minus checked) are a coverage gap for manual review, never a pass.</summary>
public sealed record CalculationCoverage(int Applicable, int Checked, int Incorrect)
{
    internal bool Valid => Applicable > 0 && Checked <= Applicable && Incorrect >= 0 && Incorrect <= Checked;
}

public sealed record EvaluationMessage(string Role, string Text);

/// <summary>One failed call retained before a bounded retry; delays are separate from call latency.</summary>
public sealed record EvaluationRetry(string Stage, string? CaseId, int? Repetition, int Number,
    double DelaySeconds, EvaluationStep Call);

/// <summary>Fixed human-labelled controls measure known error detection and false alarms, not general judge accuracy.</summary>
/// <remarks>An advisory control is reported in every run but does not gate calibration: it records a blind spot every judge tried shares.</remarks>
public sealed record CalibrationSample(string Id, string Request, ReviewText[] Texts,
    ExpectedHebrewIssue[] ExpectedIssues, bool AllowUnexpectedFindings = false, bool Advisory = false)
{
    // Record equality compares arrays by reference; reports need the captured fields and policy to match.
    internal bool HasSameContent(CalibrationSample? other) => other is not null &&
        Id == other.Id && Request == other.Request && AllowUnexpectedFindings == other.AllowUnexpectedFindings && Advisory == other.Advisory &&
        other.Texts is not null && Texts.SequenceEqual(other.Texts) &&
        other.ExpectedIssues is not null && ExpectedIssues.SequenceEqual(other.ExpectedIssues);
}
public sealed record ExpectedHebrewIssue(string Path, string Quote)
{
    /// <summary>Match whole tokens or a short containing phrase on the same field, excluding unchanged offending text.</summary>
    public bool Matches(HebrewIssue issue) => Path == issue.Path && Math.Abs(Quote.Length - issue.Quote.Length) <= 60 &&
        (ContainsPhrase(Quote, issue.Quote) || ContainsPhrase(issue.Quote, Quote)) && !ContainsPhrase(issue.Suggestion, Quote);

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
    public EvaluationStep Call { get; set; } = Call;
    public HebrewIssue[]? Issues { get; set; }
    /// <summary>Detection counts are unavailable when the judge did not return a valid review.</summary>
    public int? MissingExpectedIssueCount => Call.ContractValid && Issues is not null
        ? Sample.ExpectedIssues.Count(expected => !Issues.Any(expected.Matches)) : null;
    public int? UnexpectedFindingCount => Call.ContractValid && Issues is not null
        ? Issues.Count(issue => !Sample.ExpectedIssues.Any(expected => expected.Matches(issue))) : null;
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
    /// <summary>Parent-reported effort. Missing values remain unknown, including for unsuccessful trials.</summary>
    public int? CorrectionCount { get; set; }
    public double? TimeToReadySeconds { get; set; }

    /// <summary>Validates optional scores, bounded notes and finite parent-reported effort for files and UI edits.</summary>
    public void Validate()
    {
        if (Scores().Values.Any(score => score is < 0 or > 2) || Notes?.Length > 4000 || CorrectionCount is < 0 or > 10000 ||
            TimeToReadySeconds is { } seconds && (!double.IsFinite(seconds) || seconds is < 0 or > 604800))
            throw new InvalidDataException("Human review requires scores of 0–2, notes up to 4,000 characters, 0–10,000 corrections and 0–604,800 seconds; missing values stay null.");
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
