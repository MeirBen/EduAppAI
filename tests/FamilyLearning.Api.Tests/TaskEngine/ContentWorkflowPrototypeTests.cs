using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Evaluation;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ContentWorkflowPrototypeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"prototype-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("generated", 3)]
    [InlineData("fixed", 2)]
    [InlineData("none", 2)]
    public async Task Matched_variants_capture_exact_stages_sources_and_unknown_usage(string kind, int calls)
    {
        var plan = kind == "generated" ? Reading() : kind == "fixed" ? Supplied() : Numeric();
        var questions = Questions(kind == "generated" ? "text-input" : "numeric-input");
        var combined = Serialize(new { materials = kind == "generated" ? Materials().Materials : [], questions.Title, questions.Instructions, questions.Questions });
        using var chat = new AiFixtures.ScriptedChat(kind == "generated"
            ? [combined, Serialize(Materials()), Serialize(questions)] : [combined, Serialize(questions)]);
        var scenario = new EvaluationCase("case", "", "review", 2, questions.Questions[0].Interaction.Type, null, null, null, InitialPlan: plan);
        var report = new EvaluationReport([scenario], 1, "fixture", []) { Prototype = true, MaxCalls = calls, CallDelaySeconds = 0 };
        Directory.CreateDirectory(directory);
        await EvaluationRunner.RunAsync(chat, new(), report, directory, default);
        Assert.Equal("completed", report.Status);
        Assert.Equal(calls, report.AttemptedCalls);
        Assert.Equal(2, report.PrototypeResults.Count);
        Assert.Equal(2, report.AutomaticPasses);
        var first = report.PrototypeResults[0];
        var split = report.PrototypeResults[1];
        Assert.Equal(first.InputFingerprint, split.InputFingerprint);
        Assert.All(report.Steps, step => { Assert.NotNull(step.Output); Assert.NotNull(step.Schema); Assert.NotNull(step.RequestSha256); Assert.NotNull(step.SchemaSha256); });
        if (kind != "generated")
        {
            var materialSchema = first.Stages[0].Call!.Schema!.Value.GetProperty("properties").GetProperty("materials");
            Assert.Equal(0, materialSchema.GetProperty("maxItems").GetInt32());
            Assert.False(materialSchema.GetProperty("items").GetProperty("properties").GetProperty("id").TryGetProperty("enum", out _));
        }
        Assert.Null(report.ReportedCostCredits);
        Assert.Null(first.Review.Hebrew);
        Assert.False(report.ComparativeValueEstablished);
        var blind = await File.ReadAllTextAsync(Path.Combine(directory, "blind-review.json"));
        Assert.DoesNotContain("promptVersion", blind);
        Assert.DoesNotContain("variant", blind);
        using var review = JsonDocument.Parse(blind);
        var requirements = review.RootElement[0].GetProperty("requirements");
        Assert.Equal(first.Input.Guidance, requirements.GetProperty("guidance").GetString());
        Assert.Equal(first.Input.Questions.Guidance, requirements.GetProperty("questions").GetProperty("guidance").GetString());
        Assert.Equal(first.Input.Materials.Length, requirements.GetProperty("materials").GetArrayLength());
        Assert.DoesNotContain(MaterialId, blind);
        Assert.DoesNotContain(ControlId, blind);
        if (kind == "fixed") Assert.All(report.PrototypeResults, result => Assert.Equal(Source, Assert.Single(result.Document.Materials).Body));
        if (kind != "generated") Assert.Contains(split.Stages, stage => stage.Stage == "materials" && stage.Outcome == "skipped");
        var read = await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
        Assert.Equal(calls, read.AttemptedCalls);
    }

    [Fact]
    public async Task Failed_question_stage_checkpoints_material_and_never_replays_or_repairs_automatically()
    {
        var scenario = new EvaluationCase("reading", "", "review", 2, "text-input", null, null, null, InitialPlan: Reading());
        using var chat = new AiFixtures.ScriptedChat("{}", Serialize(Materials()), "{}");
        var report = new EvaluationReport([scenario], 1, "fixture", []) { Prototype = true, MaxCalls = 3, CallDelaySeconds = 0 };
        Directory.CreateDirectory(directory);
        await EvaluationRunner.RunAsync(chat, new(), report, directory, default);
        var result = report.PrototypeResults[1];
        Assert.Single(result.Document.Materials);
        Assert.Empty(result.Document.Questions);
        Assert.Equal(["accepted", "failed"], result.Stages.Select(s => s.Outcome));
        Assert.Equal(3, chat.Requests.Count);
        Assert.NotNull(result.Stages[1].Call!.Output);
        Assert.Equal(0, report.AutomaticPasses);
    }

    [Fact]
    public async Task Prototype_preview_resolves_fixed_fixtures_and_exact_call_budget_without_provider()
    {
        var plan = await EvaluationPlan.LoadAsync(new(["all"], 3, false, 21, Prototype: true));
        Assert.Equal(3, plan.Cases.Length);
        Assert.Equal(21, plan.PlannedCalls);
        Assert.All(plan.Cases, item => Assert.NotNull(item.InitialPlan));
        plan.ValidateBudget();
        await Assert.ThrowsAsync<ArgumentException>(() => EvaluationPlan.LoadAsync(new(["all"], 1, true, 21, Prototype: true)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_prototype_case_is_independent_of_option_order(bool caseFirst)
    {
        var options = EvaluationOptions.Parse(caseFirst
            ? ["--case", "prototype-numeric", "--prototype"]
            : ["--prototype", "--case", "prototype-numeric"]);
        Assert.True(options.Prototype);
        Assert.Equal("prototype-numeric", options.Case);
        Assert.Equal("all", EvaluationOptions.Parse(["--prototype"]).Case);
    }

    [Fact]
    public async Task Cost_budget_reserves_unknown_usage_and_stops_before_an_unaffordable_call()
    {
        var scenario = new EvaluationCase("numeric", "", "review", 2, "numeric-input", null, null, null, InitialPlan: Numeric());
        using var chat = new AiFixtures.ScriptedChat(Serialize(new { materials = Array.Empty<object>(), Questions().Title, Questions().Instructions, Questions().Questions }));
        var report = new EvaluationReport([scenario], 1, "fixture", [])
        {
            Prototype = true,
            MaxCalls = 2,
            CallDelaySeconds = 0,
            Experiment = new("test-free-model", 2, 0.01m, 0, 1, "3 cases × 3 repetitions", "unseen sources reserved for later confirmation",
                "All six dimensions must score ready", "Randomized blinded parent review", "At most 50% additional cost and latency", "No automatic repair; keep accepted material")
        };
        Directory.CreateDirectory(directory);
        await EvaluationRunner.RunAsync(chat, new() { MaxOutputTokens = 8192 }, report, directory, default);
        Assert.Single(chat.Requests);
        Assert.Equal("cost-limit", report.Status);
        Assert.Null(report.ReportedCostCredits);
        Assert.True(report.ReservedCostUsd > 0);
    }

    [Theory]
    [InlineData("null-stage")]
    [InlineData("invalid-outcome")]
    [InlineData("duplicate-trial")]
    [InlineData("forged-input")]
    public async Task Malformed_prototype_evidence_is_rejected_before_summary(string fault)
    {
        await Matched_variants_capture_exact_stages_sources_and_unknown_usage("none", 2);
        var path = Path.Combine(directory, "run.json");
        var root = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var results = root["prototypeResults"]!.AsArray();
        var result = results[0]!;
        if (fault == "null-stage") result["stages"]![0] = null;
        if (fault == "invalid-outcome") result["stages"]![0]!["outcome"] = "invented";
        if (fault == "duplicate-trial") results[1] = result.DeepClone();
        if (fault == "forged-input") result["input"]!["goal"] = "unmatched";
        await File.WriteAllTextAsync(path, root.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
