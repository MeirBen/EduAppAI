using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ContentGenerationTests
{
    private static readonly TaskDocument Empty = new("", null, [], []);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Authoring_normalizes_identity_computes_changes_and_excludes_client_metadata()
    {
        var proposal = Reading() with { Materials = [Reading().Materials[0] with { Id = null, Controls = [] }] };
        using var chat = new AiFixtures.ScriptedChat(Serialize(new { result = new { proposal, clarification = (string?)null }, assumptions = new[] { "עברית" } }));
        using var service = Service(chat);
        var result = await service.AuthorAsync(new TemplateAuthoringInput("רעיון", RequestId: "client-only", BaseRevision: 42), default);
        Assert.Empty(LearningPlanValidator.Validate(result.Value.Proposal));
        Assert.NotEmpty(result.Value.Changes);
        Assert.DoesNotContain("client-only", chat.Requests[0].Input);
        AssertVersion(result.Metadata, chat.Requests[0].Options!, "author");
    }

    [Fact]
    public async Task Clarification_is_one_call_and_pending_context_is_bounded_before_AI()
    {
        using var chat = new AiFixtures.ScriptedChat("""{"result":{"proposal":null,"clarification":"לאיזה גיל?"},"assumptions":[]}""");
        using var service = Service(chat);
        var result = await service.AuthorAsync(new TemplateAuthoringInput("רעיון", Context: [new("parent", "בקשה קודמת")]), default);
        Assert.Null(result.Value.Proposal);
        Assert.Equal("לאיזה גיל?", result.Value.Clarification);
        Assert.Single(chat.Requests);
        await Assert.ThrowsAsync<TaskValidationException>(() => service.AuthorAsync(
            new TemplateAuthoringInput("רעיון", Context: Enumerable.Repeat(new AuthoringTurn("parent", "a"), 7).ToArray()), default));
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("{\"proposal\":null,\"clarification\":null,\"assumptions\":[]}")]
    [InlineData("{\"result\":{\"proposal\":null,\"clarification\":null},\"assumptions\":[]}")]
    [InlineData("{\"result\":{\"proposal\":{},\"clarification\":null},\"assumptions\":[]}")]
    [InlineData("{\"result\":{\"proposal\":null,\"clarification\":\"\"},\"assumptions\":[]}")]
    [InlineData("{\"result\":{\"proposal\":null,\"clarification\":\"   \"},\"assumptions\":[]}")]
    [InlineData("{\"result\":{\"proposal\":null,\"clarification\":\"לאיזה גיל?\"}}")]
    [InlineData("{\"result\":{\"proposal\":null,\"clarification\":\"לאיזה גיל?\"},\"assumptions\":null}")]
    [InlineData("{\"result\":{\"proposal\":null,\"clarification\":\"לאיזה גיל?\"},\"assumptions\":[\"\"]}")]
    [InlineData("{\"result\":{\"clarification\":\"לאיזה גיל?\"},\"assumptions\":[]}")]
    [InlineData("{\"result\":null,\"assumptions\":[]}")]
    [InlineData("{\"assumptions\":[]}")]
    public async Task Empty_authoring_responses_are_rejected_without_retry(string output)
    {
        using var chat = new AiFixtures.ScriptedChat(output);
        using var service = Service(chat);
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(
            new("פעילות קריאה בעברית לכיתה ג׳ בנושא חלל, בערך 300 מילים ו־5 שאלות אמריקאיות."), default));
        Assert.Equal(502, error.StatusCode);
        Assert.Equal("invalid-output", error.Category);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task Authoring_rejects_a_complete_proposal_with_a_clarification()
    {
        using var chat = new AiFixtures.ScriptedChat(Serialize(new
        {
            result = new { proposal = Numeric(), clarification = "לאיזה גיל?" },
            assumptions = Array.Empty<string>()
        }));
        using var service = Service(chat);
        await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new("רעיון"), default));
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task Retained_fixed_source_cannot_be_rewritten_by_authoring()
    {
        var plan = Supplied();
        var changed = plan with { Materials = [plan.Materials[0] with { Text = "changed" }] };
        using var chat = new AiFixtures.ScriptedChat(Serialize(new { result = new { proposal = changed, clarification = (string?)null }, assumptions = Array.Empty<string>() }));
        using var service = Service(chat);
        await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new TemplateAuthoringInput("שינוי", plan), default));
    }

    [Theory]
    [InlineData("generated", 3)]
    [InlineData("fixed", 1)]
    [InlineData("none", 1)]
    public async Task Stage_selection_uses_only_necessary_calls_and_exact_sources(string source, int expectedCalls)
    {
        var request = Resolve(source == "generated" ? Reading() : source == "fixed" ? Supplied() : Numeric());
        using var chat = new AiFixtures.ScriptedChat(source == "generated"
            ? [Serialize(MaterialIdeaTests.Ideas()), Serialize(Materials()), Serialize(Questions("text-input"))] : [Serialize(Questions())]);
        using var service = Service(chat);
        var document = TaskAssembly.CreateDocument(request);
        if (TaskAssembly.PrepareMaterials(request, document) is { } materials)
        {
            var ideas = await service.GenerateMaterialIdeasAsync(materials, [], default);
            var selected = MaterialIdeas.Select(ideas.Value, 0);
            var result = await service.GenerateMaterialsAsync(materials, selected, default);
            document = TaskAssembly.AcceptMaterials(request, document, result.Value, result.Metadata, selected).Document!;
        }
        var questions = await service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(request, document), [], default);
        document = TaskAssembly.AcceptQuestions(request, document, questions.Value, questions.Metadata);
        Assert.Equal(expectedCalls, chat.Requests.Count);
        Assert.Empty(TaskDocumentValidator.ValidateRelease(request, document));
        Assert.All(document.Questions, q => Assert.Equal(document.Materials.Select(m => new MaterialRevision(m.Id, m.Revision)), q.Acceptance!.Sources));
        Assert.All(document.Questions, q => Assert.Equal(questions.Metadata, q.Origin.Generation));
        Assert.DoesNotContain("engineRevision", chat.Requests[^1].Input);
        Assert.DoesNotContain("inputFingerprint", chat.Requests[^1].Input);
        Assert.DoesNotContain("\"defaults\"", chat.Requests[^1].Input);
        if (source == "fixed") Assert.Equal(Source, Assert.Single(document.Materials).Body);
        if (source == "generated") Assert.Null(TaskAssembly.PrepareMaterials(request, document));
        AssertVersion(questions.Metadata, chat.Requests[^1].Options!, "questions");
        Assert.Empty(TaskAssembly.CreateDocument(request).Questions);
    }

    [Fact]
    public async Task Strict_material_failure_stops_questions_and_retains_prior_content()
    {
        var plan = Reading() with { Materials = [Reading().Materials[0] with { Length = new("range", Lower: 100, Upper: 150) }] };
        var request = Resolve(plan);
        using var chat = new AiFixtures.ScriptedChat(Serialize(Materials()));
        using var service = Service(chat);
        var document = TaskAssembly.CreateDocument(request);
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.GenerateMaterialsAsync(
            TaskAssembly.PrepareMaterials(request, document)!, new("רעיון", "מבנה"), default));
        Assert.NotEmpty(error.ValidationErrors!);
        Assert.Single(chat.Requests);
        Assert.Empty(document.Materials);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.PrepareQuestions(request, document));
    }

    [Fact]
    public void One_stale_material_regenerates_the_generated_batch_and_manual_strict_failure_blocks_questions()
    {
        var plan = Reading() with { Materials = [Reading().Materials[0], Reading().Materials[0] with { Id = OtherId, Controls = [] }] };
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, Empty, new([new(MaterialId, null, "א"), new(OtherId, null, "ב")])).Document!;
        document.Materials[0] = document.Materials[0] with { Acceptance = null };
        Assert.NotNull(TaskAssembly.PrepareMaterials(request, document));
        Assert.Null(TaskAssembly.AcceptMaterials(request, document, new([new(MaterialId, null, "ג")])).Document);
        Assert.NotNull(TaskAssembly.AcceptMaterials(request, document, new([new(MaterialId, null, "ג"), new(OtherId, null, "ד")])).Document);
        var strict = Resolve(Reading() with { Materials = [Reading().Materials[0] with { Length = new("range", Lower: 100, Upper: 150) }] });
        var manual = new TaskDocument("", null, [new(MaterialId, 1, null, "קצר", new("manual"), new(TaskRequestResolver.Fingerprint(strict), []))], []);
        Assert.Throws<TaskValidationException>(() => TaskAssembly.PrepareQuestions(strict, manual));
    }

    [Fact]
    public async Task Idea_schema_bounds_alternatives_before_writing()
    {
        var request = Resolve(Reading());
        using var chat = new AiFixtures.ScriptedChat(Serialize(MaterialIdeaTests.Ideas()));
        using var service = Service(chat);
        var result = await service.GenerateMaterialIdeasAsync(TaskAssembly.PrepareMaterials(request, Empty)!, [], default);
        Assert.Equal(5, result.Value.Ideas.Length);
        using var input = JsonDocument.Parse(chat.Requests[0].Input.Split('\n')[^1]);
        Assert.Equal(request.Goal, input.RootElement.GetProperty("request").GetProperty("goal").GetString());
        var schema = Assert.IsType<ChatResponseFormatJson>(chat.Requests[0].Options!.ResponseFormat).Schema!.Value
            .GetProperty("properties").GetProperty("ideas");
        Assert.Equal(5, schema.GetProperty("minItems").GetInt32());
        Assert.Equal(5, schema.GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public async Task Question_failure_keeps_accepted_material_and_failure_never_claims_persistence()
    {
        var request = Resolve(Reading());
        using var chat = new AiFixtures.ScriptedChat(Serialize(Materials()), "{}");
        using var service = Service(chat);
        var result = await service.GenerateMaterialsAsync(TaskAssembly.PrepareMaterials(request, Empty)!, new("רעיון", "מבנה"), default);
        var document = TaskAssembly.AcceptMaterials(request, Empty, result.Value, result.Metadata).Document!;
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(request, document), [], default));
        Assert.DoesNotContain("לא נשמר", error.Message);
        Assert.Equal("שלום עולם", Assert.Single(document.Materials).Body);
        Assert.Equal(2, chat.Requests.Count);
    }

    [Fact]
    public async Task Scoped_repair_reads_other_questions_as_context_and_preserves_them_and_task_fields()
    {
        var request = Resolve(Reading());
        var document = TaskAssembly.AcceptMaterials(request, Empty, Materials()).Document!;
        document = TaskAssembly.AcceptQuestions(request, document, Questions("text-input"));
        document.Questions[1] = document.Questions[1] with { Prompt = "other-incomplete-question", Answer = null };
        var target = document.Questions[0];
        var replacement = Question("text-input") with { Prompt = "שאלה חדשה" };
        using var chat = new AiFixtures.ScriptedChat(Serialize(replacement));
        using var service = Service(chat);
        var input = new QuestionReplacementInput(request, document, target.Id, "ניסוח אחר");
        var result = await service.ReplaceQuestionAsync(input, default);
        var changed = TaskAssembly.ReplaceQuestion(input, result.Value, result.Metadata);
        Assert.Equal(document.Title, changed.Title);
        Assert.Equal(document.Instructions, changed.Instructions);
        Assert.Equal(target.Id, changed.Questions[0].Id);
        Assert.Equal(Serialize(document.Questions[1]), Serialize(changed.Questions[1]));
        // The model sees the rest of the activity so the replacement stays distinct and consistent; identities stay app-owned.
        using var sent = JsonDocument.Parse(chat.Requests[0].Input.Split('\n')[^1]);
        Assert.Equal("other-incomplete-question", Assert.Single(sent.RootElement.GetProperty("otherQuestions").EnumerateArray()).GetProperty("prompt").GetString());
        Assert.Equal(document.Instructions, sent.RootElement.GetProperty("learnerInstructions").GetString());
        Assert.Equal(document.Materials.Single().Body, Assert.Single(sent.RootElement.GetProperty("materials").EnumerateArray()).GetProperty("body").GetString());
        Assert.All(document.Questions, question => Assert.DoesNotContain(question.Id, chat.Requests[0].Input));
        Assert.Equal(document.Materials.Select(m => new MaterialRevision(m.Id, m.Revision)), changed.Questions[0].Acceptance!.Sources);
    }

    [Fact]
    public async Task Material_repair_rejects_forged_ID_and_supplied_source_before_call()
    {
        var request = Resolve(Reading());
        var document = TaskAssembly.AcceptMaterials(request, Empty, Materials()).Document!;
        using var chat = new AiFixtures.ScriptedChat(Serialize(new MaterialCandidate(OtherId, null, "חדש")));
        using var service = Service(chat);
        await Assert.ThrowsAsync<AiGenerationException>(() => service.ReplaceMaterialAsync(new(request, document, MaterialId), default));
        var supplied = Resolve(Supplied());
        await Assert.ThrowsAsync<TaskValidationException>(() => service.ReplaceMaterialAsync(new(supplied, TaskAssembly.CreateDocument(supplied), MaterialId), default));
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("סיפור חדש", true)]
    [InlineData("חדש", false)]
    public async Task Material_repair_enforces_length_and_preserves_unrelated_content(string body, bool valid)
    {
        var plan = Reading() with
        {
            Materials = [Reading().Materials[0] with { Length = new("range", Lower: 2, Upper: 3) },
                Supplied().Materials[0] with { Id = OtherId }]
        };
        var request = Resolve(plan);
        var document = TaskAssembly.AcceptMaterials(request, TaskAssembly.CreateDocument(request), Materials()).Document!;
        document = TaskAssembly.AcceptQuestions(request, document, Questions("text-input"));
        document.Questions[1] = document.Questions[1] with { Prompt = "unrelated-private-question", Answer = null };
        var original = Serialize(document);
        using var chat = new AiFixtures.ScriptedChat(Serialize(new MaterialCandidate(MaterialId, null, body)));
        using var service = Service(chat);
        var input = new MaterialReplacementInput(request, document, MaterialId);
        if (valid)
        {
            var result = await service.ReplaceMaterialAsync(input, default);
            var changed = TaskAssembly.ReplaceMaterial(input, result.Value, result.Metadata);
            Assert.Equal(body, changed.Materials[0].Body);
            Assert.Equal(document.Materials[0].Revision + 1, changed.Materials[0].Revision);
            Assert.Equal(result.Metadata, changed.Materials[0].Origin.Generation);
            Assert.Equal(Serialize(document.Materials[1]), Serialize(changed.Materials[1]));
            Assert.Equal(Serialize(document.Questions), Serialize(changed.Questions));
            Assert.Equal(document.Title, changed.Title);
            Assert.Equal(document.Instructions, changed.Instructions);
            Assert.Contains(TaskDocumentValidator.ValidateDraft(request, changed).Diagnostics.Keys,
                key => key.StartsWith("questions[0]", StringComparison.Ordinal));
        }
        else await Assert.ThrowsAsync<AiGenerationException>(() => service.ReplaceMaterialAsync(input, default));
        Assert.Equal(original, Serialize(document));
        Assert.DoesNotContain("unrelated-private-question", Assert.Single(chat.Requests).Input);
    }

    [Fact]
    public async Task Question_repair_rejects_a_whole_document_or_model_owned_dependencies()
    {
        var request = Resolve(Numeric());
        var document = TaskAssembly.AcceptQuestions(request, Empty, Questions());
        var forged = JsonSerializer.SerializeToNode(Question())!;
        forged["sources"] = new JsonArray();
        using var chat = new AiFixtures.ScriptedChat(Serialize(Questions()), forged.ToJsonString());
        using var service = Service(chat);
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<AiGenerationException>(() => service.ReplaceQuestionAsync(new(request, document, document.Questions[0].Id), default));
    }

    [Fact]
    public async Task Question_repair_cannot_claim_dependencies_on_a_source_not_sent()
    {
        var request = Resolve(Supplied());
        var document = TaskAssembly.AcceptQuestions(request, TaskAssembly.CreateDocument(request), Questions());
        document = document with { Materials = [] };
        using var chat = new AiFixtures.ScriptedChat(Serialize(Question()));
        using var service = Service(chat);
        await Assert.ThrowsAsync<TaskValidationException>(() => service.ReplaceQuestionAsync(new(request, document, document.Questions[0].Id), default));
        Assert.Empty(chat.Requests);
    }

    internal static AiGenerationService Service(IChatClient chat, AiGenerationOptions? options = null) =>
        new([chat], NullLogger<AiGenerationService>.Instance, Options.Create(options ?? new()));
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static MaterialCandidateBatch Materials() => new([new(MaterialId, null, "שלום עולם")]);
    internal static QuestionCandidate Question(string format = "numeric-input") => new("כמה?", new(format, format == "single-choice" ? ["1", "2", "3"] : null), new("1"), 1);
    internal static QuestionCandidateBatch Questions(string format = "numeric-input", int count = 2) => new("כותרת", "ענו", Enumerable.Range(0, count).Select(_ => Question(format)).ToArray());
    private static void AssertVersion(GenerationMetadata metadata, ChatOptions options, string stage)
    {
        Assert.Equal($"content-first-{stage}-v{EngineVersions.Revision}", metadata.PromptVersion);
        Assert.Equal(metadata.PromptVersion.Replace('-', '_'), Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat).SchemaName);
        Assert.Equal(EngineVersions.Revision, metadata.EngineRevision);
        Assert.Equal(EngineVersions.SchemaVersion, metadata.SchemaVersion);
        Assert.True(options.Tools is null or { Count: 0 });
    }
}
