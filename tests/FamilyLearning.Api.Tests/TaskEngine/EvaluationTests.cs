using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Null(result.PassageWordCount); // No length requirement in this case.
        Assert.Contains("דינוזאורים", result.Generation.Request[1].Text);
        Assert.StartsWith("template-authoring-", result.Authoring.Metadata!.PromptVersion);
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

    [Theory]
    [InlineData("כותרת\n\n", 99, 99, false)]
    [InlineData("כותרת\r\n", 100, 100, true)]
    [InlineData("", 100, 100, true)]
    [InlineData("כותרת\n\n", 150, 150, true)]
    [InlineData("כותרת\n\n", 151, 151, false)]
    [InlineData("כותרת ", 99, 100, true)] // Same-line text is body text, not a heading.
    [InlineData("כותרת אחרת\n\n", 99, 101, true)] // Do not guess which other lines are headings.
    public async Task Passage_length_excludes_only_an_exact_standalone_task_title(string prefix, int bodyWords, int expectedWords, bool passes)
    {
        var content = AiFixtures.Content();
        content["title"] = "כותרת";
        content["contentBlocks"]![0]!["text"] = prefix + string.Join(' ', Enumerable.Repeat("מילה", bodyWords));
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), content.ToJsonString());
        var report = await RunAsync(chat, Case with { MinPassageWords = 100, MaxPassageWords = 150 });

        var result = Assert.Single(report.Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.Equal(passes, result.Checks["passageLength"]);
        Assert.Equal(content.ToJsonString(), result.Generation.Output);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
        Assert.True(saved.RootElement.GetProperty("results")[0].TryGetProperty("passageWordCount", out var count));
        Assert.Equal(expectedWords, count.GetInt32());
    }

    [Theory]
    [InlineData(false, 0, null)]
    [InlineData(true, 5, 5)]
    public async Task Passage_measurement_covers_empty_and_multiple_blocks(bool hasPassages, int expectedWords, int? measuredWords)
    {
        var content = AiFixtures.Content();
        content["title"] = "כותרת";
        content["contentBlocks"] = hasPassages ? new JsonArray(
            new JsonObject { ["type"] = "text", ["text"] = "כותרת" },
            new JsonObject { ["type"] = "text", ["text"] = "קטע ראשון." },
            new JsonObject { ["type"] = "text", ["text"] = "קטע שני קצר." }) : new JsonArray();
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), content.ToJsonString());
        var report = await RunAsync(chat, Case with { MinPassageWords = expectedWords, MaxPassageWords = expectedWords });

        Assert.True(Assert.Single(report.Results).Checks[hasPassages ? "passageLength" : "noPassage"]);
        var saved = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(measuredWords, Assert.Single(saved.Results).PassageWordCount);
    }

    [Fact]
    public async Task No_passage_requirement_rejects_even_a_standalone_heading_without_measuring_words()
    {
        var content = AiFixtures.Content();
        content["contentBlocks"]![0]!["text"] = content["title"]!.GetValue<string>();
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), content.ToJsonString());
        var report = await RunAsync(chat, Case with { MaxPassageWords = 0 });
        var result = Assert.Single(report.Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.False(result.Checks["noPassage"]);
        Assert.Null(result.PassageWordCount);
    }

    [Theory]
    [InlineData(2, false, null)]
    [InlineData(3, false, 2)]
    [InlineData(3, true, null)]
    public async Task Repeated_answer_position_is_advisory_and_never_changes_option_order(int count, bool vary, int? expectedPosition)
    {
        var content = AiFixtures.Content(count: count);
        var definition = AiFixtures.Definition();
        definition["instanceParameters"]![1]!["default"] = count;
        foreach (var question in content["questions"]!.AsArray())
        {
            question!["interaction"] = new JsonObject { ["type"] = "single-choice", ["options"] = new JsonArray("א", "ב", "ג") };
            question["answer"]!["value"] = "ב";
        }
        if (vary) content["questions"]![0]!["answer"]!["value"] = "א";
        using var chat = new AiFixtures.ScriptedChat(definition.ToJsonString(), content.ToJsonString());
        var report = await RunAsync(chat, Case with { QuestionCount = count, Interaction = "single-choice", ChoiceCount = 3 });

        Assert.Equal(1, report.AutomaticPasses);
        var result = Assert.Single(report.Results);
        Assert.Equal(content.ToJsonString(), result.Generation!.Output);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
        Assert.True(saved.RootElement.GetProperty("results")[0].TryGetProperty("repeatedAnswerPosition", out var position));
        Assert.Equal(expectedPosition, position.ValueKind == JsonValueKind.Null ? null : position.GetInt32());
    }

    [Theory]
    [InlineData("קהל היעד הוא כיתה ג׳. עם ", false)]
    [InlineData("יש לכתוב על theme.", false)]
    [InlineData("יש להשתמש ב-theme וב-counter.", false)]
    [InlineData("theme discount", false)]
    [InlineData("theme count_extra", false)]
    [InlineData("theme Count", false)]
    [InlineData("יש להשתמש ב־'theme' וב־'count'.", true)]
    [InlineData("theme: הנושא; count: מספר השאלות.", true)]
    [InlineData("יש ליצור שתי שאלות.", true, true)]
    public async Task Parameter_reference_check_requires_every_exact_key_without_blocking_evidence(
        string instructions, bool expected, bool noParameters = false)
    {
        var definition = AiFixtures.Definition();
        definition["generation"]!["instructions"] = instructions;
        if (noParameters)
        {
            definition["instanceParameters"] = new JsonArray();
            definition["generation"]!["questionCountParameter"] = null;
        }
        using var chat = new AiFixtures.ScriptedChat(definition.ToJsonString(), AiFixtures.Content().ToJsonString());
        var report = await RunAsync(chat);

        var result = Assert.Single(report.Results);
        Assert.True(result.Authoring!.ContractValid);
        Assert.True(result.Generation!.ContractValid);
        Assert.Equal(expected, result.Checks["parameterReferences"]);
        Assert.Equal(expected ? 1 : 0, report.AutomaticPasses);
        using var output = JsonDocument.Parse(result.Authoring.Output!);
        Assert.Equal(instructions, output.RootElement
            .GetProperty("generation").GetProperty("instructions").GetString());
        var saved = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(expected, Assert.Single(saved.Results).Checks["parameterReferences"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public async Task New_checks_do_not_make_legacy_automatic_scores_directly_comparable(int? oldVersion)
    {
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString());
        var current = await RunAsync(chat);
        var path = Path.Combine(directory, "run.json");
        var legacyJson = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        legacyJson.AsObject().Remove("automaticChecksVersion");
        if (oldVersion.HasValue) legacyJson["automaticChecksVersion"] = oldVersion.Value;
        legacyJson["results"]![0]!["checks"]!.AsObject().Remove("parameterReferences");
        legacyJson["results"]![0]!.AsObject().Remove("passageWordCount");
        legacyJson["results"]![0]!.AsObject().Remove("repeatedAnswerPosition");
        await File.WriteAllTextAsync(path, legacyJson.ToJsonString());
        var legacy = await EvaluationFiles.ReadReportAsync(path);
        Assert.Null(Assert.Single(legacy.Results).PassageWordCount);

        var comparison = EvaluationComparison.Compare(legacy, current);
        Assert.False(comparison.DirectlyComparable);
        Assert.Contains("automatic-checks-version", comparison.Incompatibilities);
        Assert.True(EvaluationComparison.Compare(legacy, legacy).DirectlyComparable);
        Assert.True(EvaluationComparison.Compare(current, current).DirectlyComparable);
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
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString())
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
            Assert.Null(result.Generation);
        });
        Assert.Equal(new ReportedTotal(null, 0, 2), EvaluationSummary.Create(report).CostCredits);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pause_separates_all_provider_stages_outside_the_request_deadline(bool judge)
    {
        var responses = judge ? await CalibrationResponsesAsync() : [];
        responses.AddRange([AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString()]);
        if (judge) responses.Add("""{"issues":[]}""");
        using var chat = new AiFixtures.ScriptedChat(responses.ToArray());
        var progress = new List<EvaluationProgress>();
        var report = await RunAsync(chat, judge: judge, callDelaySeconds: 1, progress: progress.Add,
            options: new AiGenerationOptions { RequestTimeoutSeconds = 1 });

        Assert.Equal("completed", report.Status);
        Assert.Equal(responses.Count, report.AttemptedCalls);
        Assert.Equal(responses.Count - 1, progress.Count(item => item.Stage == "waiting"));
        var steps = report.Steps.ToArray();
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
        using var chat = new AiFixtures.ScriptedChat(AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString());
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
    [InlineData("--repeat", "0")]
    [InlineData("--repeat", "6")]
    [InlineData("--max-calls", "101")]
    [InlineData("--max-calls", "0")]
    [InlineData("--call-delay-seconds", "-1")]
    [InlineData("--call-delay-seconds", "61")]
    [InlineData("--call-delay-seconds", "0.5")]
    [InlineData("--unexpected", "x")]
    public void Invalid_cli_arguments_fail_before_any_calls(string flag, string value) =>
        Assert.Throws<ArgumentException>(() => EvaluationOptions.Parse([flag, value]));

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void Call_pause_has_a_conservative_default_and_can_be_adjusted(int seconds)
    {
        Assert.Equal(5, EvaluationOptions.Parse([]).CallDelaySeconds);
        Assert.Equal(seconds, EvaluationOptions.Parse(["--call-delay-seconds", seconds.ToString()]).CallDelaySeconds);
    }

    [Fact]
    public async Task Call_plan_includes_each_stage_and_loaded_controls()
    {
        var options = EvaluationOptions.Parse(["--case", "reading-grade3", "--repeat", "3", "--max-calls", "4"]);
        Assert.False(options.Live);
        var basic = await EvaluationPlan.LoadAsync(new([options.Case], options.Repeat, options.Judge, options.MaxCalls));
        Assert.Equal(6, basic.PlannedCalls);
        Assert.Throws<ArgumentException>(basic.ValidateBudget);
        var judged = await EvaluationPlan.LoadAsync(new(["reading-grade3"], 1, true, 7));
        Assert.Equal(7, judged.PlannedCalls);
        judged.ValidateBudget();
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
    [InlineData("""{"issues":[{"path":"question,","quote":"להסיין","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"path":"question","quote":"מילה שלא הופיעה","suggestion":"להסיק","reason":"כתיב","kind":"spelling"}]}""")]
    [InlineData("""{"issues":[{"path":"question","quote":"להסיין","suggestion":"להסיין","reason":"כתיב","kind":"spelling"}]}""")]
    public async Task Judge_rejects_unverifiable_findings(string response)
    {
        using var chat = new AiFixtures.ScriptedChat(response);
        await Assert.ThrowsAsync<AiGenerationException>(() => HebrewJudge.ReviewAsync(chat, "תרגול קריאה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    [Fact]
    public async Task Judge_constrains_paths_to_each_requests_sources_without_shared_schema_mutation()
    {
        using var chat = new AiFixtures.ScriptedChat("""{"issues":[]}""", """{"issues":[]}""");
        await HebrewJudge.ReviewAsync(chat, "בקשה", [new("template.instructions", "הוראות")], 8192, CancellationToken.None);
        await HebrewJudge.ReviewAsync(chat, "בקשה", [new("task.sentences[0]", "משפט")], 8192, CancellationToken.None);

        var expectedPaths = new[] { "template.instructions", "task.sentences[0]" };
        for (var index = 0; index < expectedPaths.Length; index++)
        {
            var request = chat.Requests[index];
            var format = Assert.IsType<ChatResponseFormatJson>(request.Options!.ResponseFormat);
            var path = format.Schema!.Value.GetProperty("properties").GetProperty("issues")
                .GetProperty("items").GetProperty("properties").GetProperty("path");
            Assert.True(path.TryGetProperty("enum", out var allowed));
            Assert.Equal(expectedPaths[index], Assert.Single(allowed.EnumerateArray()).GetString());
            Assert.Contains(format.Schema.Value.ToString(), request.Input);
        }
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
            MaxCalls = maxCalls,
            CallDelaySeconds = callDelaySeconds,
            CalibrationSha256 = judge ? controls.Sha256 : "",
            CalibrationSamples = judge ? controls.Items : []
        };
        await EvaluationRunner.RunAsync(chat, options ?? new AiGenerationOptions(), report, directory, ct, progress, timeProvider);
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
