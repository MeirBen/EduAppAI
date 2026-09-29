using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Evaluation;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class EvaluationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"learning-evaluation-{Guid.NewGuid():N}");
    private static readonly EvaluationCase Case = new("reading", "צרו תבנית הבנת הנקרא.",
        "בדקו התאמת מין ומספר והיצמדות לקטע.", 2, "text-input", null, null, null);

    [Fact]
    public async Task Runs_both_real_engine_stages_and_leaves_quality_unscored()
    {
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString());
        var report = await RunAsync(chat);

        Assert.Equal("completed", report.Status);
        Assert.Equal(2, chat.Requests.Count);
        var result = Assert.Single(report.Results);
        Assert.True(result.Authoring!.ContractValid);
        Assert.True(result.Generation!.ContractValid);
        Assert.All(result.Checks.Values, Assert.True);
        Assert.Null(result.Review.Hebrew);
        Assert.Null(result.Review.Correctness);
        Assert.Contains("דינוזאורים", result.Generation.Request[1].Text);
        Assert.StartsWith("template-authoring-", result.Authoring.Metadata!.PromptVersion);
        Assert.NotEmpty(result.Authoring.Request[0].Text);
        Assert.Null(result.Authoring.CostCredits);
        Assert.True(File.Exists(Path.Combine(directory, "run.json")));
        Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
        Assert.Equal(64, report.CalibrationSha256.Length);
        Assert.Equal(HebrewJudge.Version, report.JudgePromptVersion);
        var reloaded = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(report.CalibrationSha256, reloaded.CalibrationSha256);
    }

    [Fact]
    public async Task Invalid_authoring_is_recorded_and_does_not_trigger_generation()
    {
        using var chat = new AiFixtures.ScriptedChat("{broken");
        var report = await RunAsync(chat);

        var result = Assert.Single(report.Results);
        Assert.False(result.Authoring!.ContractValid);
        Assert.Equal("{broken", result.Authoring.Output);
        Assert.Equal(502, result.Authoring.StatusCode);
        Assert.Null(result.Generation);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task Measures_adherence_separately_from_contract_validity()
    {
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString());
        var report = await RunAsync(chat, Case with { QuestionCount = 4, Interaction = "single-choice", ChoiceCount = 4 });

        var result = Assert.Single(report.Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.Contains(false, result.Checks.Values);
    }

    [Fact]
    public async Task Maximum_count_uses_the_generated_binding_and_app_parameter_validator()
    {
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), AiFixtures.Content(count: 20).ToJsonString());
        var report = await RunAsync(chat, Case with { UseMaximumQuestionCount = true, QuestionCount = 20 });

        var result = Assert.Single(report.Results);
        Assert.Equal(20, result.Parameters!["count"].GetInt32());
        Assert.True(result.Generation!.ContractValid);
    }

    [Fact]
    public async Task Cancellation_flushes_the_current_attempt_and_stops()
    {
        using var cancellation = new CancellationTokenSource();
        using var chat = new AiFixtures.ScriptedChat
        {
            BeforeResponse = token =>
            {
                cancellation.Cancel();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        var report = await RunAsync(chat, ct: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("cancelled", report.Status);
        Assert.Single(chat.Requests);
        Assert.Equal("cancelled", Assert.Single(report.Results).Authoring!.Failure);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
        Assert.Equal("cancelled", saved.RootElement.GetProperty("status").GetString());
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json")));
        Assert.Equal("cancelled", summary.RootElement.GetProperty("status").GetString());
        Assert.Null(summary.RootElement.GetProperty("costCredits").GetProperty("knownTotal").GetString());
    }

    [Fact]
    public async Task Rate_limits_stop_the_run_without_retries_or_provider_exception_text()
    {
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = System.Net.HttpStatusCode.TooManyRequests };
        var report = await RunAsync(chat, repeat: 2);

        Assert.Equal("rate-limited", report.Status);
        Assert.Single(report.Results);
        Assert.DoesNotContain("provider secret", await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
    }

    [Fact]
    public async Task Runtime_call_ceiling_stops_before_sending_even_if_the_plan_is_wrong()
    {
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString());
        var report = await RunAsync(chat, maxCalls: 1);

        Assert.Equal("call-limit", report.Status);
        Assert.Single(chat.Requests);
        Assert.Equal(1, report.AttemptedCalls);
        Assert.Equal("call-limit", Assert.Single(report.Results).Generation!.Failure);
    }

    [Theory]
    [InlineData("--repeat", "0")]
    [InlineData("--repeat", "6")]
    [InlineData("--max-calls", "101")]
    [InlineData("--max-calls", "0")]
    [InlineData("--unexpected", "x")]
    public void Invalid_cli_arguments_fail_before_any_calls(string flag, string value) =>
        Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse([flag, value]));

    [Fact]
    public void Call_plan_includes_each_stage_and_loaded_controls()
    {
        var options = EvaluationOptions.Parse(["--case", "all", "--max-calls", "4"]);
        Assert.False(options.Live);
        Assert.Equal(32, options.PlannedCalls(16, 0));
        Assert.Equal(7, EvaluationOptions.Parse(["--judge", "--max-calls", "7"]).PlannedCalls(1, 4));
    }

    [Fact]
    public async Task Judge_flags_are_advisory_and_never_rewrite_the_generated_content()
    {
        var definition = AiFixtures.Definition();
        var content = AiFixtures.Content();
        content["questions"]![0]!["prompt"] = "מה אפשר להסיין?";
        var responses = await CalibrationResponsesAsync();
        responses.AddRange([definition.ToJsonString(), content.ToJsonString(),
            """{"issues":[{"path":"task.questions[0].prompt","quote":"להסיין","suggestion":"להסיק","reason":"שגיאת כתיב","kind":"invented-word"}]}"""]);
        using var chat = new AiFixtures.ScriptedChat(responses.ToArray());
        var report = await RunAsync(chat, judge: true);

        Assert.Equal(responses.Count, report.AttemptedCalls);
        Assert.Equal(responses.Count, report.PlannedCalls);
        Assert.All(report.Calibration, result => Assert.True(result.Passed));
        Assert.True(report.JudgeCalibrationPassed);
        Assert.Equal(0, report.CalibrationFailureCount);
        Assert.True(report.HasHebrewFindings);
        Assert.Equal(1, report.GeneratedHebrewIssueCount);
        Assert.Equal(1, report.AutomaticPasses);
        var result = Assert.Single(report.Results);
        Assert.Equal("להסיין", Assert.Single(result.Issues!).Quote);
        using var unchanged = JsonDocument.Parse(result.Generation!.Output!);
        Assert.Equal("מה אפשר להסיין?", unchanged.RootElement.GetProperty("questions")[0].GetProperty("prompt").GetString());
        Assert.Null(result.Review.Hebrew);
    }

    [Fact]
    public async Task Judge_calibration_exposes_misses_and_false_alarms()
    {
        var responses = await CalibrationResponsesAsync();
        responses[0] = """{"issues":[]}""";
        responses[^1] = """{"issues":[{"path":"task.questions[0].options[1]","quote":"שלושה ילדות","suggestion":"שלוש ילדות","reason":"התאמת מספר","kind":"agreement"}]}""";
        responses.AddRange([AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString(), """{"issues":[]}"""]);
        using var chat = new AiFixtures.ScriptedChat(responses.ToArray());
        var report = await RunAsync(chat, judge: true);

        Assert.Equal(2, report.CalibrationFailureCount);
        Assert.False(report.JudgeCalibrationPassed);
        Assert.False(report.HasHebrewFindings);
    }

    [Theory]
    [InlineData("{}")] // Missing issues is not a clean review.
    [InlineData("{\"issues\":null}")]
    [InlineData("{\"issues\":[null]}")]
    [InlineData("""{"issues":[{"path":"missing","quote":"להסיין","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"path":"question","quote":"מילה שלא הופיעה","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"path":"question","quote":"להסיין","suggestion":"להסיין","reason":"כתיב","kind":"spelling"}]}""")]
    public async Task Judge_rejects_unverifiable_findings(string response)
    {
        using var chat = new AiFixtures.ScriptedChat(response);
        await Assert.ThrowsAsync<AiGenerationException>(() => HebrewJudge.ReviewAsync(chat, "תרגול קריאה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("style")]
    [InlineData("Spelling")]
    public async Task Judge_rejects_missing_or_unknown_issue_kinds(string? kind)
    {
        var response = JsonSerializer.Serialize(new { issues = new[] { new { path = "question", quote = "להסיין", suggestion = "להסיק", reason = "כתיב", kind } } });
        using var chat = new AiFixtures.ScriptedChat(response);
        await Assert.ThrowsAsync<AiGenerationException>(() => HebrewJudge.ReviewAsync(chat, "בקשה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    [Theory]
    [InlineData("spelling")]
    [InlineData("invented-word")]
    [InlineData("agreement")]
    [InlineData("grammar-syntax")]
    [InlineData("language-mixing")]
    [InlineData("non-idiomatic")]
    public async Task Judge_accepts_only_the_documented_issue_vocabulary(string kind)
    {
        var response = JsonSerializer.Serialize(new { issues = new[] { new { path = "question", quote = "להסיין", suggestion = "להסיק", reason = "כתיב", kind } } });
        using var chat = new AiFixtures.ScriptedChat(response);
        var result = await HebrewJudge.ReviewAsync(chat, "בקשה", [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None);
        Assert.Equal(kind, Assert.Single(result.Value.Issues).Kind);
    }

    private async Task<EvaluationReport> RunAsync(IChatClient chat, EvaluationCase? scenario = null,
        int repeat = 1, CancellationToken ct = default, bool judge = false, int maxCalls = 100)
    {
        Directory.CreateDirectory(directory);
        var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
        var report = new EvaluationReport([scenario ?? Case], repeat, "test-suite", new Dictionary<string, string?>())
        {
            JudgeEnabled = judge,
            MaxCalls = maxCalls,
            CalibrationSha256 = controls.Sha256,
            CalibrationSamples = controls.Items
        };
        await EvaluationRunner.RunAsync(chat, new AiGenerationOptions(), report, directory, ct);
        return report;
    }

    private static async Task<List<string>> CalibrationResponsesAsync()
    {
        var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
        return controls.Items.Select(sample => JsonSerializer.Serialize(new HebrewReview(sample.ExpectedIssues.Select(expected =>
            new HebrewIssue(expected.Path, expected.Quote, "נוסח מתוקן", "ליקוי לשוני", "grammar-syntax")).ToArray()),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))).ToList();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

}
