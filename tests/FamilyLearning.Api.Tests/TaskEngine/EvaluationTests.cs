using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
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
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString());
        var report = await RunAsync(chat);

        Assert.Equal("completed", report.Status);
        Assert.Equal(2, chat.Requests.Count);
        var result = Assert.Single(report.Results);
        Assert.True(result.Authoring!.ContractValid);
        Assert.True(result.Generation!.ContractValid);
        Assert.All(result.Checks.Values, Assert.True);
        Assert.Null(result.Review.Hebrew);
        Assert.Null(result.Review.Correctness);
        Assert.Null(result.PassageWordCount); // No length requirement in this case.
        Assert.Contains("דינוזאורים", result.Generation.Request[1].Text);
        Assert.StartsWith("content-first-author-", result.Authoring.Metadata!.PromptVersion);
        Assert.NotEmpty(result.Authoring.Request[0].Text);
        Assert.Null(result.Authoring.CostCredits);
        Assert.True(File.Exists(Path.Combine(directory, "run.json")));
        Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
        Assert.Empty(report.CalibrationSamples);
        Assert.Empty(report.CalibrationSha256);
        Assert.Empty(report.JudgePromptVersion);
        var reloaded = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(report.CalibrationSha256, reloaded.CalibrationSha256);
    }

    [Fact]
    public async Task Generated_materials_keep_idea_evidence_and_the_selected_idea_separate_from_writing()
    {
        var plan = LearningPlanFixture.Reading() with
        {
            Materials = [LearningPlanFixture.Reading().Materials[0] with { Length = null }]
        };
        string Material(string body) => JsonSerializer.Serialize(new
        {
            materials = new[] { new { id = LearningPlanFixture.MaterialId, title = "קריאה", body } }
        });
        var (material, polished) = (Material("שלום עולם"), Material("שלום לכולם"));
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.MaterialIdeas(), material, polished,
            StructuredEvaluationTests.Questions("text-input"))
        { Usage = new() { InputTokenCount = 12, CachedInputTokenCount = 8, AdditionalCounts = new() { [AiCallUsage.CacheWriteTokensKey] = 4 } } };
        var report = await RunAsync(chat, StructuredEvaluationTests.Fixed(plan));
        var result = Assert.Single(report.Results);
        var saved = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var ideas = saved.GetProperty("materialIdeas");

        Assert.Equal(4, report.PlannedCalls);
        Assert.Equal(4, report.AttemptedCalls);
        Assert.Equal("material-ideas", ideas.GetProperty("role").GetString());
        Assert.True(ideas.GetProperty("applied").GetBoolean());
        Assert.Equal($"content_first_material_ideas_v{EngineVersions.Revision}", ideas.GetProperty("schemaName").GetString());
        Assert.Equal("רעיון 2", saved.GetProperty("selectedMaterialIdea").GetProperty("premise").GetString());
        Assert.Equal("רעיון 2", result.Materials!.EffectiveInput!.Value.GetProperty("idea").GetProperty("premise").GetString());
        Assert.Equal("רעיון 2", saved.GetProperty("document").GetProperty("materials")[0].GetProperty("idea").GetProperty("premise").GetString());
        // One run holds both versions: the writing step keeps the first text and the document the polished one.
        using var written = JsonDocument.Parse(result.Materials.Output!);
        Assert.Equal("שלום עולם", written.RootElement.GetProperty("materials")[0].GetProperty("body").GetString());
        Assert.Equal("שלום לכולם", Assert.Single(result.Document!.Materials).Body);
        Assert.True(result.MaterialPolish!.Applied);
        Assert.Equal(new[] { "material-ideas", "materials", "material-polish", "questions" }, report.Steps.Where(step => step.RequestSent).Select(step => step.Role));
        var summary = EvaluationSummary.Create(report);
        Assert.Equal(new StageCounts(1, 1, 0, 0), summary.MaterialIdeas);
        Assert.Equal(new StageCounts(1, 1, 0, 0), summary.MaterialPolish);
        Assert.Equal(4, summary.InputTokens.KnownCalls);
        Assert.Equal(new ReportedTotal(32, 4, 0), summary.CacheReadTokens);
        Assert.Equal(new ReportedTotal(16, 4, 0), summary.CacheWriteTokens);
        Assert.Equal(8, ideas.GetProperty("cacheReadTokens").GetInt64());
        Assert.Equal(4, ideas.GetProperty("cacheWriteTokens").GetInt64());
        Assert.True(result.EndToEndReady);
        await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
    }

    [Fact]
    public async Task Invalid_ideas_stop_before_writing_and_preserve_failure_evidence()
    {
        using var chat = new AiFixtures.ScriptedChat("""{"ideas":[]}""");
        var report = await RunAsync(chat, StructuredEvaluationTests.Fixed(LearningPlanFixture.Reading()));
        var result = Assert.Single(report.Results);
        var saved = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Single(chat.Requests);
        Assert.Equal("failed", saved.GetProperty("materialIdeas").GetProperty("outcome").GetString());
        Assert.Null(saved.GetProperty("selectedMaterialIdea").GetString());
        Assert.Equal("earlier-stage", result.Materials!.SkipReason);
        Assert.Equal("earlier-stage", result.Generation!.SkipReason);
        Assert.Empty(result.Document!.Materials);
        Assert.Equal(new StageCounts(1, 0, 1, 0), EvaluationSummary.Create(report).MaterialIdeas);
        Assert.False(result.EndToEndReady);
        await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
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
        Assert.Equal("skipped", result.Generation!.Outcome);
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("authoring", "settings.questionCount")]
    [InlineData("generation", "questions[0].answer")]
    [InlineData("wrong-count", "questions")]
    [InlineData("null-questions", "questions")]
    public async Task Domain_rejections_retain_safe_field_errors_in_saved_reports(string stage, string field)
    {
        var definition = EvaluationFixtures.Definition();
        var content = EvaluationFixtures.Content();
        if (stage == "authoring") definition["result"]!["proposal"]!["settings"]!["questionCount"] = 0;
        if (stage == "generation") content["questions"]![0]!["answer"]!["value"] = "";
        if (stage == "wrong-count") content = EvaluationFixtures.Content(count: 1);
        if (stage == "null-questions") content["questions"] = null;
        using var chat = new AiFixtures.ScriptedChat(definition.ToJsonString(), content.ToJsonString());
        await RunAsync(chat);

        var saved = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        var result = Assert.Single(saved.Results);
        var step = stage == "authoring" ? result.Authoring! : result.Generation!;
        Assert.False(step.ContractValid);
        Assert.Equal(502, step.StatusCode);
        var error = Assert.Single(step.ValidationErrors!);
        Assert.Equal(field, error.Key);
        var message = Assert.Single(error.Value);
        Assert.NotEmpty(message);
        Assert.DoesNotContain("provider secret", message);
        Assert.Equal(stage == "authoring" ? 1 : 2, chat.Requests.Count);
        Assert.Empty(saved.Retries);
    }

    [Fact]
    public async Task Measures_adherence_separately_from_contract_validity()
    {
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString());
        var report = await RunAsync(chat, Case with { QuestionCount = 4, Interaction = "single-choice", ChoiceCount = 4 });

        var result = Assert.Single(report.Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.Contains(false, result.Checks.Values);
    }

    [Fact]
    public async Task Recorded_check_versions_are_preserved_and_must_match_for_comparison()
    {
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString());
        var current = await RunAsync(chat);
        var path = Path.Combine(directory, "run.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["automaticChecksVersion"] = 5;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var baseline = await EvaluationFiles.ReadReportAsync(path);
        Assert.Equal(5, baseline.AutomaticChecksVersion);
        var comparison = EvaluationComparison.Compare(baseline, current);
        Assert.False(comparison.DirectlyComparable);
        Assert.Contains("automatic-checks-version", comparison.Incompatibilities);
        Assert.True(EvaluationComparison.Compare(baseline, baseline).DirectlyComparable);
        Assert.True(EvaluationComparison.Compare(current, current).DirectlyComparable);
    }

    [Fact]
    public async Task Concrete_settings_reach_generation_without_dynamic_fields()
    {
        var plan = EvaluationFixtures.Plan() with { Settings = new("חלל", "מבוגרים", "hard", 20) };
        using var chat = new AiFixtures.ScriptedChat(StructuredEvaluationTests.Proposal(plan), EvaluationFixtures.Content(count: 20).ToJsonString());
        var report = await RunAsync(chat, Case with { QuestionCount = 20 });

        var result = Assert.Single(report.Results);
        Assert.Equal(new("חלל", "מבוגרים", "hard", 20), result.Input!.Settings);
        Assert.Contains("\"topic\":\"חלל\"", chat.Requests[1].Input);
        Assert.Contains("\"audience\":\"מבוגרים\"", chat.Requests[1].Input);
        Assert.Contains("\"difficulty\":\"hard\"", chat.Requests[1].Input);
        Assert.Contains("\"questionCount\":20", chat.Requests[1].Input);
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
    public async Task Rate_limits_stop_after_three_retries_without_provider_exception_text()
    {
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = System.Net.HttpStatusCode.TooManyRequests };
        var clock = new ImmediateTimeProvider();
        var report = await RunAsync(chat, repeat: 2, timeProvider: clock);

        Assert.Equal("rate-limited", report.Status);
        Assert.Single(report.Results);
        Assert.Equal(4, report.AttemptedCalls);
        Assert.Equal(3, report.Retries.Count);
        Assert.Equal(4, chat.Requests.Count);
        Assert.Equal(3, clock.Delays.Count);
        for (var i = 0; i < clock.Delays.Count; i++)
            Assert.InRange(clock.Delays[i].TotalSeconds, 5 * Math.Pow(2, i), 6 * Math.Pow(2, i));
        Assert.DoesNotContain("provider secret", await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
    }

    [Fact]
    public async Task Successful_retry_preserves_each_failed_attempt_and_counts_it_in_the_budget()
    {
        var calls = 0;
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString())
        {
            BeforeResponse = _ => ++calls <= 2
                ? Task.FromException(new HttpRequestException("secret", null, System.Net.HttpStatusCode.TooManyRequests))
                : Task.CompletedTask
        };
        var report = await RunAsync(chat, maxCalls: 4, timeProvider: new ImmediateTimeProvider());

        Assert.Equal("completed", report.Status);
        Assert.Equal(4, report.AttemptedCalls);
        Assert.Equal(2, report.Retries.Count);
        Assert.All(report.Retries, retry => Assert.Equal(429, retry.Call.StatusCode));
        Assert.Equal(new[] { 1, 2 }, report.Retries.Select(retry => retry.Number));
        Assert.True(Assert.Single(report.Results).Authoring!.ContractValid);
        var summary = EvaluationSummary.Create(report);
        Assert.Equal(4, summary.AttemptedCalls);
        Assert.Equal(4, summary.CostCredits.MissingCalls);
        var saved = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(4, saved.AttemptedCalls);
    }

    [Fact]
    public async Task Retry_stops_at_the_total_call_budget()
    {
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = System.Net.HttpStatusCode.TooManyRequests };
        var report = await RunAsync(chat, maxCalls: 2, timeProvider: new ImmediateTimeProvider());
        Assert.Equal("call-limit", report.Status);
        Assert.Equal(2, report.AttemptedCalls);
        Assert.Equal(2, chat.Requests.Count);
        Assert.Single(report.Retries);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Cancellation_during_retry_keeps_the_last_real_attempt(bool justBeforeSending, bool judge)
    {
        using var cancellation = new CancellationTokenSource();
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = System.Net.HttpStatusCode.TooManyRequests };
        var waiting = false;
        var report = await RunAsync(chat, ct: cancellation.Token, judge: judge, progress: progress =>
        {
            if (progress.Stage == "retry-wait") waiting = true;
            if (waiting && (!justBeforeSending || progress.Stage == (judge ? "calibration" : "authoring"))) cancellation.Cancel();
        }, timeProvider: new ImmediateTimeProvider()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("cancelled", report.Status);
        Assert.Single(chat.Requests);
        Assert.Empty(report.Retries);
        Assert.Equal(429, judge ? Assert.Single(report.Calibration).Call.StatusCode : Assert.Single(report.Results).Authoring!.StatusCode);
        Assert.Equal(1, report.AttemptedCalls);
        if (judge) Assert.Equal(1, EvaluationSummary.Create(report).CalibrationCompletedCount);
        else Assert.Equal(new StageCounts(1, 0, 1, 0), EvaluationSummary.Create(report).Authoring);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest)]
    [InlineData(System.Net.HttpStatusCode.PaymentRequired)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.GatewayTimeout)]
    public async Task Non_rate_limit_failures_do_not_retry(System.Net.HttpStatusCode status)
    {
        using var chat = new AiFixtures.ScriptedChat { FailureStatus = status };
        var report = await RunAsync(chat, timeProvider: new ImmediateTimeProvider());
        Assert.Single(chat.Requests);
        Assert.Empty(report.Retries);
    }

    [Fact]
    public async Task Timeouts_do_not_retry_and_preserve_unknown_cost_as_the_next_repetition_runs()
    {
        using var chat = new AiFixtures.ScriptedChat
        {
            BeforeResponse = token => Task.Delay(Timeout.InfiniteTimeSpan, token)
        };
        var report = await RunAsync(chat, repeat: 2, options: new() { RequestTimeoutSeconds = 1 })
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("completed", report.Status);
        Assert.Equal(2, report.AttemptedCalls);
        Assert.Empty(report.Retries);
        Assert.All(report.Results, result =>
        {
            Assert.Equal(504, result.Authoring!.StatusCode);
            Assert.False(result.Authoring.ContractValid);
            Assert.Null(result.Authoring.CostCredits);
            Assert.Equal("skipped", result.Generation!.Outcome);
        });
        Assert.Equal(new ReportedTotal(null, 0, 2), EvaluationSummary.Create(report).CostCredits);
    }

    [Fact]
    public async Task Runtime_call_ceiling_stops_before_sending_even_if_the_plan_is_wrong()
    {
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.Definition().ToJsonString());
        var report = await RunAsync(chat, maxCalls: 1);

        Assert.Equal("call-limit", report.Status);
        Assert.Single(chat.Requests);
        Assert.Equal(1, report.AttemptedCalls);
        Assert.Equal("call-limit", Assert.Single(report.Results).Generation!.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pause_separates_all_provider_stages_outside_the_request_deadline(bool judge)
    {
        var responses = judge ? await CalibrationResponsesAsync() : [];
        responses.AddRange([EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString()]);
        if (judge) responses.Add("""{"issues":[]}""");
        using var chat = new AiFixtures.ScriptedChat(responses.ToArray());
        var progress = new List<EvaluationProgress>();
        var report = await RunAsync(chat, judge: judge, callDelaySeconds: 1, progress: progress.Add,
            options: new AiGenerationOptions { RequestTimeoutSeconds = 1 });

        Assert.Equal("completed", report.Status);
        Assert.Equal(responses.Count, report.AttemptedCalls);
        Assert.Equal(responses.Count - 1, progress.Count(item => item.Stage == "waiting"));
        var steps = report.Steps.Where(step => step.RequestSent).ToArray();
        Assert.All(steps, step => Assert.True(step.ContractValid));
        for (var i = 1; i < steps.Length; i++)
            Assert.True(steps[i].StartedAtUtc - steps[i - 1].FinishedAtUtc >= TimeSpan.FromMilliseconds(950));
        var saved = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(1, saved.CallDelaySeconds);
    }

    [Fact]
    public async Task Cancelling_the_pause_preserves_completed_work_without_sending_the_next_call()
    {
        using var cancellation = new CancellationTokenSource();
        using var chat = new AiFixtures.ScriptedChat(EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString());
        var report = await RunAsync(chat, ct: cancellation.Token, callDelaySeconds: 60, progress: progress =>
        {
            if (progress.Stage == "waiting") cancellation.Cancel();
        }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("cancelled", report.Status);
        Assert.Equal(1, report.AttemptedCalls);
        var result = Assert.Single(report.Results);
        Assert.True(result.Authoring!.ContractValid);
        Assert.False(result.Generation!.RequestSent);
        Assert.Null(result.Generation.FinishedAtUtc);
        Assert.Single(chat.Requests);
        var saved = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal("cancelled", saved.Status);
    }

    [Theory]
    [InlineData("--repeat", "6")]
    [InlineData("--max-calls", "0")]
    [InlineData("--call-delay-seconds", "0.5")]
    [InlineData("--unexpected", "x")]
    public void Invalid_cli_arguments_fail_before_any_calls(string flag, string value) =>
        Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse([flag, value]));

    [Fact]
    public async Task Call_plan_includes_each_stage_and_loaded_controls()
    {
        var options = EvaluationOptions.Parse(["--case", "reading-grade3", "--repeat", "3", "--max-calls", "4"]);
        Assert.False(options.Live);
        var basic = await EvaluationPlan.LoadAsync(new([options.Case], options.Repeat, options.Judge, options.MaxCalls));
        Assert.Equal(15, basic.PlannedCalls);
        Assert.Throws<ArgumentException>(basic.ValidateBudget);
        var judged = await EvaluationPlan.LoadAsync(new(["reading-grade3"], 1, true, 10));
        Assert.Equal(10, judged.PlannedCalls);
        judged.ValidateBudget();
        var selection = EvaluationOptions.Parse(["--case", "reading-grade3,number-gender", "--judge"]);
        var both = await EvaluationPlan.LoadAsync(new(selection.CaseIds, 1, selection.Judge, 100));
        Assert.Equal(["reading-grade3", "number-gender"], both.Cases.Select(item => item.Id));
        Assert.Equal(judged.PlannedCalls + both.Cases[1].PlannedCalls + 1, both.PlannedCalls); // One more case and review, one calibration.
        await Assert.ThrowsAsync<ArgumentException>(() => EvaluationPlan.LoadAsync(new(
            EvaluationOptions.Parse(["--case", "reading-grade3,"]).CaseIds, 1, false, 100)));
    }

    [Fact]
    public async Task Judge_flags_are_advisory_and_never_rewrite_the_generated_content()
    {
        var definition = EvaluationFixtures.Definition();
        var content = EvaluationFixtures.Content();
        content["questions"]![0]!["prompt"] = "מה אפשר להסיין?";
        var responses = await CalibrationResponsesAsync();
        responses.AddRange([definition.ToJsonString(), content.ToJsonString()]);
        using var chat = new AiFixtures.ScriptedChat(responses.ToArray())
        {
            Respond = input => Review(FieldId(input, "document.questions[0].prompt"), "להסיין", "להסיק", "invented-word")
        };
        var report = await RunAsync(chat, judge: true);

        Assert.Equal(responses.Count + 1, report.AttemptedCalls);
        Assert.Equal(responses.Count + 1, report.PlannedCalls);
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
        responses[1] = """{"issues":[]}"""; // A gating control; the first one is advisory.
        var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
        responses[^1] = Review(Array.FindIndex(controls.Items[^1].Texts, text => text.Path == "document.questions[0].options[1]"),
            "שלושה ילדות", "שלוש ילדות", "agreement");
        responses.AddRange([EvaluationFixtures.Definition().ToJsonString(), EvaluationFixtures.Content().ToJsonString(), """{"issues":[]}"""]);
        using var chat = new AiFixtures.ScriptedChat(responses.ToArray());
        var report = await RunAsync(chat, judge: true);

        Assert.Equal(2, report.CalibrationFailureCount);
        Assert.False(report.JudgeCalibrationPassed);
        Assert.False(report.HasHebrewFindings);
    }

    [Theory]
    [InlineData("{}")] // Missing issues is not a clean review.
    [InlineData("{\"issues\":[null]}")]
    [InlineData("""{"issues":[{"text":1,"quote":"להסיין","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"text":-1,"quote":"להסיין","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"text":0,"quote":"מילה שלא הופיעה","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"text":0,"quote":"להסיין","suggestion":"להסיין","reason":"כתיב","kind":"spelling"}]}""")]
    public async Task Judge_rejects_unverifiable_findings(string response)
    {
        using var chat = new AiFixtures.ScriptedChat(response);
        await Assert.ThrowsAsync<AiGenerationException>(() => HebrewJudge.ReviewAsync(chat, "תרגול קריאה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    [Fact]
    public async Task Judge_never_defaults_a_missing_field_id_to_the_first_field()
    {
        using var chat = new AiFixtures.ScriptedChat("""{"issues":[{"quote":"להסיין","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""");
        await Assert.ThrowsAsync<JsonException>(() => HebrewJudge.ReviewAsync(chat, "בקשה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    [Fact]
    public async Task Judge_sends_one_fixed_contract_and_reports_findings_by_field_path()
    {
        using var chat = new AiFixtures.ScriptedChat("""{"issues":[]}""", Review(1, "להסיין", "להסיק", "invented-word"));
        await HebrewJudge.ReviewAsync(chat, "בקשה", [new("plan.guidance", "הוראות")], 8192, CancellationToken.None);
        var review = await HebrewJudge.ReviewAsync(chat, "בקשה",
            [new("document.title", "כותרת"), new("document.materials[0].body", "אפשר להסיין")], 8192, CancellationToken.None);

        Assert.Equal(new HebrewIssue("document.materials[0].body", "להסיין", "להסיק", "ליקוי לשוני", "invented-word"), Assert.Single(review.Value.Issues));
        var schemas = chat.Requests.Select(request => Assert.IsType<ChatResponseFormatJson>(request.Options!.ResponseFormat).Schema!.Value.GetRawText());
        Assert.Single(schemas.Distinct());
        Assert.All(chat.Requests, request => Assert.Equal(HebrewJudge.Instructions, request.Input[..request.Input.IndexOf('{')].TrimEnd('\n')));
        Assert.Equal(1, FieldId(chat.Requests[1].Input, "document.materials[0].body"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("style")]
    [InlineData("Spelling")]
    public async Task Judge_rejects_missing_or_unknown_issue_kinds(string? kind)
    {
        var response = JsonSerializer.Serialize(new { issues = new[] { new { text = 0, quote = "להסיין", suggestion = "להסיק", reason = "כתיב", kind } } });
        using var chat = new AiFixtures.ScriptedChat(response);
        await Assert.ThrowsAsync<AiGenerationException>(() => HebrewJudge.ReviewAsync(chat, "בקשה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    private async Task<EvaluationReport> RunAsync(IChatClient chat, EvaluationCase? scenario = null,
        int repeat = 1, CancellationToken ct = default, bool judge = false, int maxCalls = 100,
        int callDelaySeconds = 0, Action<EvaluationProgress>? progress = null, AiGenerationOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        Directory.CreateDirectory(directory);
        var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
        var report = new EvaluationReport([scenario ?? Case], repeat, "test-suite", new Dictionary<string, string?>())
        {
            JudgeEnabled = judge,
            JudgePromptVersion = judge ? HebrewJudge.Version : "",
            JudgePrompt = judge ? HebrewJudge.Instructions : "",
            JudgeProfile = judge ? new() { ["Model"] = HebrewJudge.Model } : [],
            MaxCalls = maxCalls,
            CallDelaySeconds = callDelaySeconds,
            CalibrationSha256 = judge ? controls.Sha256 : "",
            CalibrationSamples = judge ? controls.Items : []
        };
        await EvaluationRunner.RunAsync(chat, chat, options ?? new AiGenerationOptions(), report, directory, ct, progress, timeProvider);
        return report;
    }

    private static async Task<List<string>> CalibrationResponsesAsync()
    {
        var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
        return controls.Items.Select(sample => JsonSerializer.Serialize(new
        {
            issues = sample.ExpectedIssues.Select(expected => new
            {
                text = Array.FindIndex(sample.Texts, text => text.Path == expected.Path),
                quote = expected.Quote,
                suggestion = "נוסח מתוקן",
                reason = "ליקוי לשוני",
                kind = "grammar-syntax"
            })
        })).ToList();
    }

    private static string Review(int text, string quote, string suggestion, string kind) => JsonSerializer.Serialize(new
    {
        issues = new[] { new { text, quote, suggestion, reason = "ליקוי לשוני", kind } }
    });

    /// <summary>The id the judge request assigned to <paramref name="path"/>; the instructions contain no JSON.</summary>
    private static int FieldId(string input, string path)
    {
        using var request = JsonDocument.Parse(input[input.IndexOf('{')..]);
        return request.RootElement.GetProperty("texts").EnumerateArray()
            .Single(text => text.GetProperty("path").GetString() == path).GetProperty("id").GetInt32();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

}
