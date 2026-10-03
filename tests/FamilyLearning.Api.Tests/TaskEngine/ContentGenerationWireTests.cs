using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ContentGenerationWireTests
{
    [Fact]
    public async Task Strict_wire_schemas_use_portable_forms()
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = body => body.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString()!.Contains("author")
            ? """{"result":{"proposal":null,"clarification":"לאיזה גיל?"},"assumptions":[]}"""
            : Serialize(Questions());
        using var services = local.Services("json_schema");
        var service = services.GetRequiredService<AiGenerationService>();
        await service.AuthorAsync(new("רעיון"), default);
        var request = Resolve(Supplied());
        await service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(request, TaskAssembly.CreateDocument(request)), default);
        var schemas = local.Bodies.Select(bytes =>
        {
            using var body = JsonDocument.Parse(bytes);
            return body.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").Clone();
        }).ToArray();
        Assert.Equal(2, schemas.Length);
        // OpenRouter's Gemini conversion erases an object holding an integer enum and widens a standalone "null"
        // type to a nullable string; equal bounds and ["null"] are equivalent forms that survive it.
        foreach (var schema in schemas)
        {
            var types = Types(schema).ToArray();
            Assert.DoesNotContain(types, type => type.ValueKind == JsonValueKind.String && type.GetString() == "null");
            Assert.Contains(types, type => type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Select(t => t.GetString()).SequenceEqual(["null"]));
        }
        var definitions = schemas[0].GetProperty("$defs");
        var version = definitions.GetProperty("plan").GetProperty("properties").GetProperty("schemaVersion");
        Assert.False(version.TryGetProperty("enum", out _));
        Assert.Equal(EngineVersions.SchemaVersion, version.GetProperty("minimum").GetInt32());
        Assert.Equal(EngineVersions.SchemaVersion, version.GetProperty("maximum").GetInt32());
        // Gemini expands bounded arrays when compiling strict output; bounded control and option lists exceeded
        // its schema budget (HTTP 400). LearningPlanValidator owns those limits.
        Assert.All(new[]
        {
            definitions.GetProperty("plan").GetProperty("properties").GetProperty("controls"),
            definitions.GetProperty("material").GetProperty("properties").GetProperty("controls"),
            definitions.GetProperty("questionPlan").GetProperty("properties").GetProperty("controls"),
            definitions.GetProperty("control").GetProperty("properties").GetProperty("options")
        }, list => Assert.False(list.TryGetProperty("maxItems", out _)));

        static IEnumerable<JsonElement> Types(JsonElement node) => node.ValueKind switch
        {
            JsonValueKind.Object => node.EnumerateObject().SelectMany(p => p.Name == "type" ? [p.Value, .. Types(p.Value)] : Types(p.Value)),
            JsonValueKind.Array => node.EnumerateArray().SelectMany(Types),
            _ => []
        };
    }

    [Theory]
    [InlineData("json_schema", "2", false)]
    [InlineData("json_schema", "3", true)]
    [InlineData("json_object", "2", true)]
    public async Task Strict_question_count_limit_bounds_exact_counts_in_strict_schemas_only(string mode, string limit, bool exact)
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = _ => Serialize(Questions(count: 3));
        using var services = local.Services(mode, new() { ["Ai:StrictQuestionCountLimit"] = limit });
        var request = Resolve(Numeric(3));
        await services.GetRequiredService<AiGenerationService>().GenerateQuestionsAsync(
            TaskAssembly.PrepareQuestions(request, TaskAssembly.CreateDocument(request)), default);
        using var body = JsonDocument.Parse(Assert.Single(local.Bodies));
        var root = body.RootElement;
        using var schema = JsonDocument.Parse(mode == "json_schema"
            ? root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetRawText()
            : root.GetProperty("messages")[0].GetProperty("content").GetString()!.Split("\nOutput JSON schema:\n")[1]);
        var questions = schema.RootElement.GetProperty("properties").GetProperty("questions");
        Assert.Equal(exact, questions.TryGetProperty("minItems", out _));
        Assert.Equal(exact, questions.TryGetProperty("maxItems", out _));
    }

    [Fact]
    public async Task Strict_question_count_limit_stays_within_the_product_cap()
    {
        await using var local = await LocalAiProvider.StartAsync();
        using var services = local.Services("json_schema", new() { ["Ai:StrictQuestionCountLimit"] = "21" });
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<AiGenerationService>());
    }

    [Theory]
    [InlineData("json_schema", "true")]
    [InlineData("json_object", "false")]
    public async Task Schema_in_prompt_adds_a_strict_mode_copy_but_never_removes_a_required_one(string mode, string schemaInPrompt)
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = _ => """{"result":{"proposal":null,"clarification":"לאיזה גיל?"},"assumptions":[]}""";
        using var services = local.Services(mode, new() { ["Ai:SchemaInPrompt"] = schemaInPrompt });
        await services.GetRequiredService<AiGenerationService>().AuthorAsync(new("רעיון"), default);
        using var request = JsonDocument.Parse(Assert.Single(local.Bodies));
        Assert.Contains("\nOutput JSON schema:\n", request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("json_schema")]
    [InlineData("json_object")]
    public async Task Maximum_canonical_plan_and_chat_context_fit_the_real_wire_budget(string mode)
    {
        var plan = Numeric() with
        {
            Controls = Enumerable.Range(1, 16).Select(i => new ControlDefinition(i.ToString("x32"), new string('א', 100), "text",
                new string('ב', 400), true, JsonSerializer.SerializeToElement(new string('ג', 500)), MaxLength: 500)).ToArray(),
            Materials = Enumerable.Range(17, 4).Select(i => new MaterialDefinition(i.ToString("x32"), new string('ד', 100),
                "generated", "", null, null, [])).ToArray()
        };
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var room = 24000 - JsonSerializer.Serialize(plan, json).Length;
        Assert.InRange(room, 0, 4000);
        plan = plan with { Guidance = new string('ה', room) };
        Assert.Equal(24000, JsonSerializer.Serialize(plan, json).Length);
        Assert.Empty(LearningPlanValidator.Validate(plan));
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = _ => """{"result":{"proposal":null,"clarification":"מה לשנות?"},"assumptions":[]}""";
        using var services = local.Services(mode);
        var service = services.GetRequiredService<AiGenerationService>();
        await service.AuthorAsync(new TemplateAuthoringInput(new string('ו', 4000), plan,
            Enumerable.Range(0, 6).Select(_ => new AuthoringTurn("parent", new string('ז', 2000))).ToArray()), default);
        Assert.True(Assert.Single(local.Bodies).Length < AiGenerationOptions.RequestByteLimit);
        var error = await Assert.ThrowsAsync<TaskValidationException>(() => service.AuthorAsync(new TemplateAuthoringInput("רעיון", plan with { Guidance = plan.Guidance + "ה" }), default));
        Assert.NotEmpty(error.Errors);
        Assert.Single(local.Bodies);
    }

    [Fact]
    public async Task Concurrent_requests_keep_their_own_counts_formats_schemas_and_sources()
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = body =>
        {
            using var input = JsonDocument.Parse(body.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var request = input.RootElement.GetProperty("request");
            return Serialize(Questions(request.GetProperty("questions").GetProperty("formats")[0].GetString()!, request.GetProperty("settings").GetProperty("questionCount").GetInt32()));
        };
        using var services = local.Services();
        var service = services.GetRequiredService<AiGenerationService>();
        var first = Resolve(Supplied());
        var second = Resolve(Mixed(true));
        var calls = await Task.WhenAll(service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(first, TaskAssembly.CreateDocument(first)), default),
            service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(second, TaskAssembly.CreateDocument(second)), default));
        Assert.Equal([2, 3], calls.Select(c => c.Value.Questions.Length));
        foreach (var bytes in local.Bodies)
        {
            using var body = JsonDocument.Parse(bytes);
            var root = body.RootElement;
            Assert.False(root.TryGetProperty("tools", out _));
            using var input = JsonDocument.Parse(root.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var request = input.RootElement.GetProperty("request");
            var count = request.GetProperty("settings").GetProperty("questionCount").GetInt32();
            var schema = root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");
            Assert.Equal(count, schema.GetProperty("properties").GetProperty("questions").GetProperty("minItems").GetInt32());
            if (count == 2) Assert.Equal(Source, input.RootElement.GetProperty("materials")[0].GetProperty("body").GetString());
        }
    }

    [Fact]
    public async Task Output_schema_UTF8_limit_is_inclusive_even_when_the_schema_travels_in_the_prompt()
    {
        await using var local = await LocalAiProvider.StartAsync();
        using var services = local.Services("json_object");
        var client = services.GetRequiredService<IChatClient>();
        var baseline = JsonSerializer.Serialize(new { type = "object", description = "" });
        foreach (var extra in new[] { 0, 1 })
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object", description = new string('a', 65536 - Encoding.UTF8.GetByteCount(baseline) + extra) });
            var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema) };
            if (extra == 0) await client.GetResponseAsync([new(ChatRole.User, "test")], options);
            else Assert.Equal("schema-limit", (await Assert.ThrowsAsync<AiGenerationException>(() => client.GetResponseAsync([new(ChatRole.User, "test")], options))).Category);
        }
        Assert.Single(local.Bodies);
    }

    [Fact]
    public async Task Compiled_HTTP_body_limit_counts_both_schema_copies_and_accepts_exact_boundary()
    {
        await using var local = await LocalAiProvider.StartAsync();
        using var services = local.Services();
        var client = services.GetRequiredService<IChatClient>();
        var schema = JsonSerializer.SerializeToElement(new { type = "object", description = new string('a', 32000) });
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema) };
        var prompt = schema.GetRawText();
        await client.GetResponseAsync([new(ChatRole.System, prompt), new(ChatRole.User, "")], options);
        var overhead = Assert.Single(local.Bodies).Length;
        Assert.True(overhead > 64000);
        var padding = new string('a', 512 * 1024 - overhead);
        await client.GetResponseAsync([new(ChatRole.System, prompt), new(ChatRole.User, padding)], options);
        Assert.Equal(512 * 1024, local.Bodies[1].Length);
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => client.GetResponseAsync([new(ChatRole.System, prompt), new(ChatRole.User, padding + "a")], options));
        Assert.Equal("request-limit", error.Category);
        Assert.Equal(2, local.Bodies.Count);
    }

    [Theory]
    [InlineData(32000, true)]
    [InlineData(32001, false)]
    public async Task Response_character_limit_is_inclusive(int length, bool accepted)
    {
        var json = """{"result":{"proposal":null,"clarification":"איזה גיל?"},"assumptions":[]}""";
        using var chat = new AiFixtures.ScriptedChat(json.PadRight(length));
        using var service = Service(chat);
        if (accepted) await service.AuthorAsync(new TemplateAuthoringInput("רעיון"), default);
        else await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new TemplateAuthoringInput("רעיון"), default));
    }
}
