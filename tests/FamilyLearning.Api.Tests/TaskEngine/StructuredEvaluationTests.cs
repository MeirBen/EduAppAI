using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Evaluation;
using Microsoft.Extensions.AI;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class StructuredEvaluationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"structured-evaluation-{Guid.NewGuid():N}");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Fixed_plan_preview_counts_only_applicable_stages_and_never_authors()
    {
        Directory.CreateDirectory(directory);
        var scenario = Fixed(Supplied());
        await File.WriteAllTextAsync(Path.Combine(directory, "cases.json"), JsonSerializer.Serialize(new[] { scenario }, JsonOptions));
        var plan = await EvaluationPlan.LoadAsync(new(["all"], 2, false, 2), directory);
        Assert.Equal(2, plan.PlannedCalls);
        plan.ValidateBudget();
        using var chat = new AiFixtures.ScriptedChat(Questions());
        var report = await Run(chat, scenario);
        Assert.Single(chat.Requests);
        Assert.True(Assert.Single(report.Results).Generation!.ContractValid);
        var saved = JsonSerializer.SerializeToElement(report, JsonOptions).GetProperty("results")[0];
        Assert.Equal("skipped", saved.GetProperty("authoring").GetProperty("outcome").GetString());
        Assert.Equal("skipped", saved.GetProperty("materials").GetProperty("outcome").GetString());
        Assert.Equal(Source, saved.GetProperty("document").GetProperty("materials")[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Per_task_source_and_source_revision_are_retained_in_exact_question_evidence()
    {
        var plan = Supplied("per-task");
        var scenario = Fixed(plan) with
        {
            InitialInput = new(plan.Defaults,
            MaterialInputs: Json(JsonSerializer.Serialize(new Dictionary<string, object> { [MaterialId] = new { sourceText = Source } })))
        };
        using var chat = new AiFixtures.ScriptedChat(Questions());
        var result = Assert.Single((await Run(chat, scenario)).Results);
        Assert.Equal("no-generated-materials", result.Materials!.SkipReason);
        Assert.Equal(Source, result.Document!.Materials[0].Body);
        var source = Assert.Single(result.Generation!.Sources);
        Assert.Equal(MaterialId, source.Id);
        Assert.Equal(result.Document.Materials[0].Revision, source.Revision);
        Assert.Equal(Source, result.Generation.EffectiveInput!.Value.GetProperty("materials")[0].GetProperty("body").GetString());
        Assert.Equal(64, result.Generation.RequestSha256!.Length);
        Assert.Equal(64, result.Generation.SchemaSha256!.Length);
    }

    [Theory]
    [InlineData("target", true)]
    [InlineData("range", false)]
    public async Task Target_is_advisory_while_a_strict_range_rejects_the_same_candidate(string mode, bool accepted)
    {
        var length = mode == "range" ? new LengthExpectation(mode, Lower: 100, Upper: 150) : new(mode, new(100, false));
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Length = length }] };
        using var chat = new AiFixtures.ScriptedChat(JsonSerializer.Serialize(new
        {
            materials = new[]
        { new { id = MaterialId, title = "כותרת", body = string.Join(' ', Enumerable.Repeat("מילה", 99)) } }
        }), Questions("text-input"));
        var result = Assert.Single((await Run(chat, Fixed(plan))).Results);
        Assert.Equal(accepted, result.Materials!.Applied);
        Assert.Equal(accepted, result.EndToEndReady);
        if (accepted) Assert.Null(Assert.Single(result.Measurements).Satisfied);
        else Assert.NotNull(result.Materials.Candidate);
    }

    [Fact]
    public async Task Final_clarification_does_not_generate_from_an_older_accepted_plan()
    {
        var scenario = new EvaluationCase("clarify", "תרגול", "בירור", 2, "numeric-input", null, null, 0)
        { Refinements = ["שנה את זה"] };
        using var chat = new AiFixtures.ScriptedChat(Proposal(Numeric()),
            """{"result":{"proposal":null,"clarification":"מה לשנות?"},"assumptions":[]}""");
        var result = Assert.Single((await Run(chat, scenario)).Results);
        Assert.Equal(2, chat.Requests.Count);
        Assert.NotNull(result.Plan);
        Assert.False(result.InterpretationPassed);
        Assert.Equal("clarification", result.Refinements[0].Outcome);
        Assert.False(result.Refinements[0].Applied);
        Assert.Equal("skipped", result.Generation!.Outcome);
    }

    [Fact]
    public async Task Clarification_retains_original_context_and_refinement_uses_the_accepted_plan()
    {
        var scenario = new EvaluationCase("author", "ליצור תרגול", "שימור הבקשה", 2, "numeric-input", null, null, 0)
        { Refinements = ["חשבון לכיתה ג", "לשנות את הנושא"] };
        using var chat = new AiFixtures.ScriptedChat(
            """{"result":{"proposal":null,"clarification":"איזה נושא?"},"assumptions":[]}""",
            Proposal(Numeric()), Proposal(Numeric() with { Name = "מעודכן" }), Questions());
        var report = await Run(chat, scenario);
        Assert.Equal(4, report.AttemptedCalls);
        Assert.Contains("ליצור תרגול", chat.Requests[1].Input);
        Assert.Contains("איזה נושא?", chat.Requests[1].Input);
        using var refinement = JsonDocument.Parse(report.Results[0].Refinements[1].Request[^1].Text);
        Assert.Equal("מספרים", refinement.RootElement.GetProperty("baseDefinition").GetProperty("name").GetString());
        Assert.Empty(refinement.RootElement.GetProperty("context").EnumerateArray());
        Assert.Equal("מעודכן", Assert.Single(report.Results).Plan!.Name);
        Assert.True(report.Results[0].EndToEndReady);
    }

    [Fact]
    public async Task Strict_material_failure_keeps_candidate_and_skips_questions()
    {
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Length = new("range", Lower: 100, Upper: 150) }] };
        var scenario = Fixed(plan) with { Interaction = "text-input", MinPassageWords = 100, MaxPassageWords = 150 };
        using var chat = new AiFixtures.ScriptedChat(JsonSerializer.Serialize(new { materials = new[] { new { id = MaterialId, title = "כותרת", body = "שלום עולם" } } }));
        var report = await Run(chat, scenario);
        var result = Assert.Single(report.Results);
        Assert.Single(chat.Requests);
        Assert.False(result.Materials!.Applied);
        Assert.NotNull(result.Materials.Candidate);
        Assert.Equal("OpenRouter", result.Materials.Provider);
        Assert.Equal("test-free-model", result.Materials.Metadata!.Model);
        Assert.NotEmpty(result.Materials.ValidationErrors!);
        Assert.Equal("skipped", result.Generation!.Outcome);
        Assert.Empty(result.Document!.Materials);
        Assert.Null(result.Materials.CostCredits);
    }

    [Fact]
    public async Task Rejected_authoring_preserves_known_response_provenance()
    {
        using var chat = new AiFixtures.ScriptedChat("not-json");
        var scenario = new EvaluationCase("invalid", "תרגול", "מבנה", 2, "numeric-input", null, null, 0);
        var step = Assert.Single((await Run(chat, scenario)).Results).Authoring!;
        Assert.False(step.ContractValid);
        Assert.Equal("OpenRouter", step.Provider);
        Assert.Equal("test-free-model", step.Metadata!.Model);
        Assert.StartsWith("content-first-author-", step.Metadata.PromptVersion);
    }

    [Fact]
    public async Task Scoped_replacement_preserves_other_questions_and_records_selected_identity()
    {
        var scenario = Fixed(Numeric()) with { Replacements = [new("replace-question", 0, "שאלה אחרת")] };
        using var chat = new AiFixtures.ScriptedChat(Questions(), JsonSerializer.Serialize(new QuestionCandidate("חדש", new("numeric-input"), new("7"), 2), JsonOptions));
        var report = await Run(chat, scenario);
        var result = Assert.Single(report.Results);
        Assert.Equal(2, report.AttemptedCalls);
        Assert.Equal("חדש", result.Document!.Questions[0].Prompt);
        Assert.Equal("כמה הם 2+1?", result.Document.Questions[1].Prompt);
        var replacement = Assert.Single(result.Replacements);
        Assert.True(replacement.Applied);
        Assert.Equal(result.Document.Questions[0].Id, replacement.TargetId);
    }

    [Fact]
    public async Task Rejected_replacement_retains_the_accepted_document_and_known_partial_usage()
    {
        var scenario = Fixed(Numeric()) with { Replacements = [new("replace-question", 0)] };
        using var chat = new AiFixtures.ScriptedChat(Questions(),
            """{"prompt":"שאלה","interaction":{"type":"numeric-input"},"answer":{"value":"not numeric"},"points":1}""")
        { Usage = new() { InputTokenCount = 12 } };
        var result = Assert.Single((await Run(chat, scenario)).Results);
        var replacement = Assert.Single(result.Replacements);
        Assert.Equal("failed", replacement.Outcome);
        Assert.False(replacement.Applied);
        Assert.NotNull(replacement.Candidate);
        Assert.Equal(12, replacement.InputTokens);
        Assert.Null(replacement.OutputTokens);
        Assert.Null(replacement.CostCredits);
        Assert.Equal("כמה הם 1+1?", result.Document!.Questions[0].Prompt);
        Assert.True(result.GenerationPassed);
        Assert.False(result.ReplacementPassed);
        Assert.False(result.EndToEndReady);
    }

    [Fact]
    public async Task Dropped_range_demand_fails_adherence_even_when_generated_content_passes_weaker_plan()
    {
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Id = null, Length = null, Controls = [] }] };
        var scenario = new EvaluationCase("range", "קטע באורך 100–150 מילים", "טווח קבוע", 2, "text-input", null, 100, 150)
        { ExpectedGeneratedMaterials = 1, ExpectedLength = new("range", Lower: 100, Upper: 150) };
        using var chat = new EvaluationFixtures.Chat((index, input) => index switch
        {
            0 => Proposal(plan),
            1 => JsonSerializer.Serialize(new { materials = new[] { new { id = input.GetProperty("materials")[0].GetProperty("id").GetString(), title = "כותרת", body = string.Join(' ', Enumerable.Repeat("מילה", 120)) } } }),
            _ => Questions("text-input")
        });
        var report = await Run(chat, scenario);
        var result = Assert.Single(report.Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.True(result.Checks["passageLength"]);
        Assert.False(result.Checks["planLength"]);
        Assert.False(result.EndToEndReady);
    }

    [Theory]
    [InlineData("", 99, 99, false)]
    [InlineData("", 100, 100, true)]
    [InlineData("", 150, 150, true)]
    [InlineData("", 151, 151, false)]
    [InlineData("כותרת\n", 99, 100, true)]
    [InlineData("— 😀\n", 100, 100, true)]
    public async Task Independent_100_to_150_range_uses_shared_body_count_without_stripping_headings(string prefix, int words, int measured, bool passed)
    {
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Length = null }] };
        var body = prefix + string.Join(' ', Enumerable.Repeat("מִלָּה", words));
        var material = JsonSerializer.Serialize(new { materials = new[] { new { id = MaterialId, title = "כותרת", body } } });
        using var chat = new AiFixtures.ScriptedChat(material, Questions("text-input"));
        var report = await Run(chat, Fixed(plan) with { MinPassageWords = 100, MaxPassageWords = 150 });
        var result = Assert.Single(report.Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.Equal(measured, result.PassageWordCount);
        Assert.Equal(passed, result.Checks["passageLength"]);
        Assert.Equal(material, result.Materials!.Output);
        Assert.Equal(body, result.Document!.Materials[0].Body);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, null, true)]
    public async Task Custom_control_expectations_remain_independent_of_plan_validity(int actual, int? expected, bool passed)
    {
        var plan = EvaluationFixtures.Plan() with { Controls = actual == 0 ? [] : [new(null, "בחירה", "boolean", "אפשרות להורה", Default: Json("false"))] };
        using var chat = new AiFixtures.ScriptedChat(Proposal(plan), EvaluationFixtures.Content().ToJsonString());
        var scenario = new EvaluationCase("controls", "תרגול", "בחירות", 2, "text-input", null, null, 0, AdditionalControlCount: expected);
        var result = Assert.Single((await Run(chat, scenario)).Results);
        Assert.True(result.Generation!.ContractValid);
        Assert.Equal(passed, result.EndToEndReady);
        if (actual == 1) Assert.False(result.Input!.Controls[0].Value.GetBoolean());
    }

    private async Task<EvaluationReport> Run(IChatClient chat, EvaluationCase scenario)
    {
        Directory.CreateDirectory(directory);
        var report = new EvaluationReport([scenario], 1, "isolated", []) { MaxCalls = 10 };
        await EvaluationRunner.RunAsync(chat, new(), report, directory, CancellationToken.None);
        return await EvaluationFiles.ReadReportAsync(Path.Combine(directory, "run.json"));
    }

    internal static EvaluationCase Fixed(LearningPlan plan) => new("fixed", "", "תוכן מהתכנית", plan.Defaults.QuestionCount,
        plan.Questions.Formats[0], plan.Questions.ChoiceCount?.Value, null, null, InitialPlan: plan);
    internal static string Proposal(LearningPlan plan) => JsonSerializer.Serialize(new { result = new { proposal = plan, clarification = (string?)null }, assumptions = Array.Empty<string>() }, JsonOptions);
    internal static string Questions(string format = "numeric-input") => JsonSerializer.Serialize(new QuestionCandidateBatch("תרגול", null,
        [new("כמה הם 1+1?", new(format), new("2"), 1), new("כמה הם 2+1?", new(format), new("3"), 1)]), JsonOptions);

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
