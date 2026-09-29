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
        using var chat = new AiFixtures.ScriptedChat() { WaitForCancellation = true };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var report = await RunAsync(chat, ct: cancellation.Token);

        Assert.Equal("cancelled", report.Status);
        Assert.Single(chat.Requests);
        Assert.Equal("cancelled", Assert.Single(report.Results).Authoring!.Failure);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
        Assert.Equal("cancelled", saved.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Rate_limits_stop_the_run_without_retries_or_provider_exception_text()
    {
        using var chat = new RateLimitedChat();
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
    public void Planned_calls_must_fit_the_explicit_budget()
    {
        var options = EvaluationOptions.Parse(["--case", "all", "--max-calls", "4"]);
        Assert.False(options.Live);
        Assert.Throws<ArgumentException>(() => options.ValidateCallBudget(16));
        Assert.Equal(5, EvaluationOptions.Parse(["--judge", "--max-calls", "5"]).ValidateCallBudget(1));
    }

    [Fact]
    public async Task Judge_flags_are_advisory_and_never_rewrite_the_generated_content()
    {
        var definition = AiFixtures.Definition();
        var content = AiFixtures.Content();
        content["questions"]![0]!["prompt"] = "מה אפשר להסיין?";
        using var chat = new AiFixtures.ScriptedChat(
            """{"issues":[{"path":"task.questions[0].prompt","quote":"להסיין","suggestion":"להסיק","reason":"שגיאת כתיב"},{"path":"task.passage","quote":"הנמלות","suggestion":"הנמלים","reason":"צורת רבים שגויה"}]}""",
            """{"issues":[]}""", definition.ToJsonString(), content.ToJsonString(),
            """{"issues":[{"path":"task.questions[0].prompt","quote":"להסיין","suggestion":"להסיק","reason":"שגיאת כתיב"}]}""");
        var report = await RunAsync(chat, judge: true);

        Assert.Equal(5, report.AttemptedCalls);
        Assert.Equal(5, report.PlannedCalls);
        Assert.All(report.Calibration, result => Assert.True(result.Passed));
        Assert.False(report.JudgeChecksPassed);
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
        using var chat = new AiFixtures.ScriptedChat("""{"issues":[]}""",
            """{"issues":[{"path":"task.questions[0].options[1]","quote":"שלושה ילדות","suggestion":"שלוש ילדות","reason":"התאמת מספר"}]}""",
            AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString(), """{"issues":[]}""");
        var report = await RunAsync(chat, judge: true);

        Assert.All(report.Calibration, result => Assert.False(result.Passed));
        Assert.False(report.JudgeChecksPassed);
    }

    [Theory]
    [InlineData("{}")] // Missing issues is not a clean review.
    [InlineData("{\"issues\":null}")]
    [InlineData("{\"issues\":[null]}")]
    [InlineData("""{"issues":[{"path":"missing","quote":"להסיין","suggestion":"להסיק","reason":"כתיב"}]}""")]
    [InlineData("""{"issues":[{"path":"question","quote":"מילה שלא הופיעה","suggestion":"להסיק","reason":"כתיב"}]}""")]
    [InlineData("""{"issues":[{"path":"question","quote":"להסיין","suggestion":"להסיין","reason":"כתיב"}]}""")]
    public async Task Judge_rejects_unverifiable_findings(string response)
    {
        using var chat = new AiFixtures.ScriptedChat(response);
        await Assert.ThrowsAsync<AiGenerationException>(() => HebrewJudge.ReviewAsync(chat, "תרגול קריאה",
            [new("question", "מה אפשר להסיין?")], 8192, CancellationToken.None));
    }

    private async Task<EvaluationReport> RunAsync(IChatClient chat, EvaluationCase? scenario = null,
        int repeat = 1, CancellationToken ct = default, bool judge = false, int maxCalls = 100)
    {
        Directory.CreateDirectory(directory);
        var report = new EvaluationReport([scenario ?? Case], repeat, "test-suite", new Dictionary<string, string?>())
        { JudgeEnabled = judge, MaxCalls = maxCalls };
        await EvaluationRunner.RunAsync(chat, new AiGenerationOptions(), report, directory, ct);
        return report;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class RateLimitedChat : DelegatingChatClient
    {
        public RateLimitedChat() : base(new AiFixtures.ScriptedChat()) { }
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("provider secret", null, System.Net.HttpStatusCode.TooManyRequests);
    }
}
