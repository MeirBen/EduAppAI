using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class EvaluationReportsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"learning-reports-{Guid.NewGuid():N}");

    [Fact]
    public void Summary_keeps_stages_checks_findings_usage_and_review_coverage_separate()
    {
        var report = CreateReport(repeat: 2, judge: true);
        report.Calibration.Add(new(report.CalibrationSamples[0], Step()) { Issues = [] });
        var first = new EvaluationResult("reading", 1)
        {
            Authoring = Step(20, 8, 2, 0.2m, 200),
            Generation = Step(30, 12, null, null, 300),
            Judge = Step(40, 16, 4, 0.3m, 400),
            Issues = [Issue()]
        };
        first.Checks["questionCount"] = false;
        first.Checks["interaction"] = true;
        first.Review.Hebrew = 1;
        report.Results.Add(first);
        report.Results.Add(new("reading", 2)
        {
            Authoring = new() { RequestSent = true, FinishedAtUtc = DateTime.UtcNow, Failure = "provider-error", ElapsedMilliseconds = 500 }
        });

        var summary = EvaluationSummary.Create(report);

        Assert.Equal(5, summary.AttemptedCalls);
        Assert.Equal(new StageCounts(2, 1, 1, 0), summary.Authoring);
        Assert.Equal(new StageCounts(1, 1, 0, 1), summary.Generation);
        Assert.Equal(0, summary.ScenarioAutomaticPasses);
        Assert.Equal(1, summary.AutomaticFailuresByCheck["questionCount"]);
        Assert.True(summary.JudgeCalibrationPassed);
        Assert.Equal(0, summary.CalibrationFailureCount);
        Assert.Equal(1, summary.GeneratedHebrewIssueCount);
        Assert.Equal(1, summary.HebrewIssuesByKind["invented-word"]);
        Assert.Equal(["reading"], summary.CasesWithHebrewFindings);
        Assert.Equal(new ReportedTotal(100, 4, 1), summary.InputTokens);
        Assert.Equal(new ReportedTotal(40, 4, 1), summary.OutputTokens);
        Assert.Equal(new ReportedTotal(7, 3, 2), summary.ReasoningTokens);
        Assert.Equal(new ReportedTotal(0.6m, 3, 2), summary.CostCredits);
        Assert.Equal(250, summary.AverageLatencyMilliseconds);
        Assert.Equal(4, summary.CallsWithResponse);
        Assert.Equal(new HumanScoreSummary(1, 1, 1), summary.HumanReview["hebrew"]);
        Assert.Equal(new HumanScoreSummary(0, 2, null), summary.HumanReview["correctness"]);
    }

    [Fact]
    public void Missing_usage_and_cost_are_unknown_but_reported_zero_is_preserved()
    {
        var report = CreateReport();
        report.Results.Add(new("reading", 1)
        {
            Authoring = Step(null, null, null, null),
            Generation = Step(null, null, null, null)
        });
        var summary = EvaluationSummary.Create(report);
        Assert.Equal(new ReportedTotal(null, 0, 2), summary.CostCredits);
        Assert.Equal(new ReportedTotal(null, 0, 2), summary.InputTokens);
        Assert.Null(report.ReportedCostCredits);
        report.Results[0].Generation!.CostCredits = 0;
        Assert.Equal(new ReportedTotal(0, 1, 1), EvaluationSummary.Create(report).CostCredits);
    }

    [Fact]
    public async Task Artifacts_round_trip_and_comparison_recomputes_summary_after_human_review()
    {
        var baseline = CreateReport();
        baseline.Results.Add(new("reading", 1) { Authoring = Step(), Generation = Step() });
        baseline.Results[0].Checks["questionCount"] = false;
        baseline.Results[0].Review.Hebrew = 1;
        var candidate = CreateReport(model: "test/b");
        candidate.Results.Add(new("reading", 1) { Authoring = Step(), Generation = Step() });
        candidate.Results[0].Checks["questionCount"] = true;
        candidate.Results[0].Review.Hebrew = 2;
        candidate.Results[0].Generation!.Metadata = new("OpenRouter", "test/b", "instance-generation-v2", DateTime.UtcNow);
        var beforePath = await SaveAsync("baseline", baseline);
        var afterPath = await SaveAsync("candidate", candidate);
        await File.WriteAllTextAsync(Path.Combine(directory, "candidate", "summary.json"), "stale summary, deliberately ignored");

        var comparison = await EvaluationComparison.ReadAsync(beforePath, afterPath);

        Assert.True(comparison.DirectlyComparable);
        Assert.Empty(comparison.Incompatibilities);
        Assert.Equal(new ProfileChange("test/a", "test/b"), comparison.ProfileChanges["Model"]);
        Assert.Equal(1, comparison.Deltas["scenarioAutomaticPasses"]);
        Assert.Equal(-1, comparison.CheckFailureDeltas["questionCount"]);
        Assert.Equal(1, comparison.Deltas["humanReview.hebrew.average"]);
        Assert.True(comparison.HumanReviewComparable["hebrew"]);
        Assert.Null(comparison.Deltas["humanReview.correctness.average"]);
        Assert.Contains("instance-generation-v2", comparison.Candidate.GenerationPromptVersions);
        var loaded = await EvaluationFiles.ReadReportAsync(afterPath);
        Assert.Equal(candidate.StartedAtUtc, loaded.StartedAtUtc);
        Assert.Equal(candidate.CalibrationSha256, loaded.CalibrationSha256);
        Assert.Equal(HebrewJudge.Version, loaded.JudgePromptVersion);
        Assert.Equal(HebrewJudge.Instructions, loaded.JudgePrompt);
    }

    [Theory]
    [InlineData("suite", "case-suite-hash")]
    [InlineData("case", "selected-cases-or-order")]
    [InlineData("repeat", "repeat-count")]
    [InlineData("prompt", "scenario-inputs")]
    [InlineData("partial", "incomplete-run")]
    [InlineData("calibration", "calibration-suite-hash")]
    public void Comparison_marks_incompatible_inputs_without_selecting_a_winner(string change, string reason)
    {
        var baseline = CreateReport(judge: true);
        var candidate = CreateReport(suite: change == "suite" ? "different" : "suite", repeat: change == "repeat" ? 2 : 1,
            caseId: change == "case" ? "other" : "reading", prompt: change == "prompt" ? "בקשה אחרת" : "בקשת לימוד", judge: true,
            calibrationHash: change == "calibration" ? "other-controls" : "controls");
        if (change == "partial") candidate.Status = "cancelled";
        var comparison = EvaluationComparison.Compare(baseline, candidate);
        Assert.False(comparison.DirectlyComparable);
        Assert.Contains(reason, comparison.Incompatibilities);
        Assert.False(comparison.HebrewFindingsComparable);
    }

    [Fact]
    public async Task Changing_judge_instructions_is_visible_even_if_someone_forgets_to_bump_the_version()
    {
        var baseline = CreateReport(judge: true);
        baseline.Results.Add(new("reading", 1) { Authoring = Step(), Generation = Step() });
        var path = await SaveAsync("changed-prompt", baseline);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["judgePrompt"] = "Changed judge instructions";
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var candidate = await EvaluationFiles.ReadReportAsync(path);

        var comparison = EvaluationComparison.Compare(baseline, candidate);
        Assert.False(comparison.DirectlyComparable);
        Assert.Contains("judge-prompt", comparison.Incompatibilities);
        Assert.Equal(baseline.JudgePromptVersion, candidate.JudgePromptVersion);
    }

    [Fact]
    public async Task Missing_required_measurement_state_is_not_silently_counted_as_zero_calls()
    {
        var report = CreateReport();
        report.Results.Add(new("reading", 1) { Authoring = Step(), Generation = Step() });
        var path = await SaveAsync("missing-state", report);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["results"]![0]!["authoring"]!.AsObject().Remove("requestSent");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<JsonException>(() => EvaluationFiles.ReadReportAsync(path));
    }

    [Fact]
    public void Partial_usage_or_different_review_coverage_does_not_produce_misleading_deltas()
    {
        var before = CreateReport(judge: true);
        var after = CreateReport(judge: true);
        foreach (var report in new[] { before, after })
        {
            report.Calibration.Add(new(report.CalibrationSamples[0], Step()) { Issues = [] });
            report.Results.Add(new("reading", 1) { Authoring = Step(), Generation = Step(), Judge = Step(), Issues = [] });
        }
        before.Results[0].Issues = [Issue()];
        before.Results[0].Review.Hebrew = 1;
        after.Results[0].Judge!.ContractValid = false;
        after.Results[0].Generation!.CostCredits = null;
        var comparison = EvaluationComparison.Compare(before, after);
        Assert.True(comparison.DirectlyComparable);
        Assert.False(comparison.HebrewFindingsComparable);
        Assert.Equal(-1, comparison.Deltas["generatedHebrewIssues"]);
        Assert.Equal(-1, comparison.HebrewKindDeltas["invented-word"]);
        Assert.Null(comparison.Deltas["costCredits"]);
        Assert.False(comparison.HumanReviewComparable["hebrew"]);
        Assert.Null(comparison.Deltas["humanReview.hebrew.average"]);
    }

    [Theory]
    [InlineData("formatVersion", "1")]
    [InlineData("results[0].review.hebrew", "3")]
    [InlineData("results[0].review.hebrew", "-1")]
    public async Task Unsupported_reports_and_invalid_human_scores_fail_explicitly(string field, string value)
    {
        var report = CreateReport();
        report.Results.Add(new("reading", 1) { Authoring = Step(), Generation = Step() });
        var path = await SaveAsync("invalid", report);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (field == "formatVersion") json[field] = JsonNode.Parse(value);
        else json["results"]![0]!["review"]!["hebrew"] = JsonNode.Parse(value);
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
    }

    private async Task<string> SaveAsync(string name, EvaluationReport report)
    {
        var output = Path.Combine(directory, name);
        Directory.CreateDirectory(output);
        await EvaluationFiles.SaveAsync(report, output);
        return Path.Combine(output, "run.json");
    }

    internal static EvaluationReport CreateReport(string model = "test/a", string suite = "suite", int repeat = 1,
        string caseId = "reading", string prompt = "בקשת לימוד", bool judge = false, string calibrationHash = "controls") =>
        new([new(caseId, prompt, "בדיקת לשון", 2, "text-input", null, null, null)], repeat, suite, new() { ["Model"] = model })
        {
            Status = "completed",
            FinishedAtUtc = DateTime.UtcNow,
            CalibrationSha256 = calibrationHash,
            JudgeEnabled = judge,
            CalibrationSamples = [new("clean", "בקשה", [new("text", "משפט תקין.")], [])]
        };

    internal static EvaluationStep Step(long? input = 10, long? output = 4, long? reasoning = 1, decimal? cost = 0.1m, double latency = 100) => new()
    {
        RequestSent = true,
        ResponseReceived = true,
        FinishedAtUtc = DateTime.UtcNow,
        ContractValid = true,
        InputTokens = input,
        OutputTokens = output,
        ReasoningTokens = reasoning,
        CostCredits = cost,
        ElapsedMilliseconds = latency,
        Model = "test/actual",
        Metadata = new("OpenRouter", "test/actual", "instance-generation-v1", DateTime.UtcNow)
    };

    private static HebrewIssue Issue() => new("task.questions[0].prompt", "להסיין", "להסיק", "מילה שגויה", "invented-word");

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
