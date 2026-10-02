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
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ContentGenerationWireTests
{
    [Fact]
    public async Task Strict_authoring_wire_preserves_the_plan_and_pins_its_version_without_a_numeric_enum()
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = _ => """{"result":{"proposal":null,"clarification":"לאיזה גיל?"},"assumptions":[]}""";
        using var services = local.Services("json_schema");
        await services.GetRequiredService<AiGenerationService>().AuthorAsync(new("רעיון"), default);
        using var request = JsonDocument.Parse(Assert.Single(local.Bodies));
        var root = request.RootElement;
        var format = root.GetProperty("response_format").GetProperty("json_schema");
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.True(root.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.DoesNotContain("Output JSON schema", root.GetProperty("messages")[0].GetProperty("content").GetString());
        var plan = schema.GetProperty("$defs").GetProperty("plan");
        Assert.Equal(["schemaVersion", "name", "goal", "guidance", "defaults", "materials", "questions", "controls", "totalLength"],
            plan.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(9, plan.GetProperty("properties").EnumerateObject().Count());
        Assert.False(plan.GetProperty("additionalProperties").GetBoolean());
        var version = plan.GetProperty("properties").GetProperty("schemaVersion");
        Assert.Equal("integer", version.GetProperty("type").GetString());
        Assert.False(version.TryGetProperty("enum", out _));
        Assert.Equal(EngineVersions.SchemaVersion, version.GetProperty("minimum").GetInt32());
        Assert.Equal(EngineVersions.SchemaVersion, version.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task Strict_wire_keeps_null_only_branches_exact_through_the_Gemini_conversion()
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
        Assert.Equal(2, local.Bodies.Count);
        foreach (var bytes in local.Bodies)
        {
            using var body = JsonDocument.Parse(bytes);
            var types = Types(body.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema")).ToArray();
            // OpenRouter turns a standalone "null" type into a nullable string for Gemini; ["null"] stays null-only.
            Assert.DoesNotContain(types, type => type.ValueKind == JsonValueKind.String && type.GetString() == "null");
            Assert.Contains(types, type => type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Select(t => t.GetString()).SequenceEqual(["null"]));
        }

        static IEnumerable<JsonElement> Types(JsonElement node) => node.ValueKind switch
        {
            JsonValueKind.Object => node.EnumerateObject().SelectMany(p => p.Name == "type" ? [p.Value, .. Types(p.Value)] : Types(p.Value)),
            JsonValueKind.Array => node.EnumerateArray().SelectMany(Types),
            _ => []
        };
    }

    [Fact]
    public async Task Strict_authoring_schema_leaves_control_list_sizes_to_application_validation()
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = _ => """{"result":{"proposal":null,"clarification":"לאיזה גיל?"},"assumptions":[]}""";
        using var services = local.Services("json_schema");
        await services.GetRequiredService<AiGenerationService>().AuthorAsync(new("רעיון"), default);
        using var request = JsonDocument.Parse(Assert.Single(local.Bodies));
        var definitions = request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("$defs");
        // Gemini expands bounded arrays when compiling strict output; bounded control and option lists exceeded
        // its schema budget (HTTP 400). The validator owns these limits, including the plan-wide control total.
        Assert.All(new[]
        {
            definitions.GetProperty("plan").GetProperty("properties").GetProperty("controls"),
            definitions.GetProperty("material").GetProperty("properties").GetProperty("controls"),
            definitions.GetProperty("questionPlan").GetProperty("properties").GetProperty("controls"),
            definitions.GetProperty("control").GetProperty("properties").GetProperty("options")
        }, list => Assert.False(list.TryGetProperty("maxItems", out _)));
        var select = new ControlDefinition(ControlId, "בחירה", "select", "משמעות",
            Options: Enumerable.Range(1, 21).Select(n => new ControlOption(n.ToString())).ToArray());
        Assert.NotEmpty(LearningPlanValidator.Validate(Numeric() with { Controls = [select] }));
        Assert.Empty(LearningPlanValidator.Validate(Numeric() with { Controls = [select with { Options = select.Options![..20] }] }));
    }

    [Theory]
    [InlineData("json_schema")]
    [InlineData("json_object")]
    public async Task Empty_clarification_is_rejected_in_both_JSON_modes_without_retry(string mode)
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = _ => """{"result":{"proposal":null,"clarification":""},"assumptions":[]}""";
        using var services = local.Services(mode);
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => services.GetRequiredService<AiGenerationService>()
            .AuthorAsync(new("פעילות קריאה בעברית לכיתה ג׳ בנושא חלל, בערך 300 מילים ו־5 שאלות אמריקאיות."), default));
        Assert.Equal("invalid-output", error.Category);
        Assert.Equal(502, error.StatusCode);
        using var request = JsonDocument.Parse(Assert.Single(local.Bodies));
        Assert.Equal(mode, request.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        using var schema = ModelSchema(request.RootElement);
        Assert.Equal(1, schema.RootElement.GetProperty("properties").GetProperty("result").GetProperty("anyOf")[1]
            .GetProperty("properties").GetProperty("clarification").GetProperty("minLength").GetInt32());
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

    [Theory]
    [InlineData("json_schema")]
    [InlineData("json_object")]
    public async Task Concurrent_requests_keep_their_own_counts_formats_schemas_and_sources(string mode)
    {
        await using var local = await LocalAiProvider.StartAsync();
        local.Respond = body =>
        {
            using var input = JsonDocument.Parse(body.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var request = input.RootElement.GetProperty("request");
            return Serialize(Questions(request.GetProperty("questions").GetProperty("formats")[0].GetString()!, request.GetProperty("settings").GetProperty("questionCount").GetInt32()));
        };
        using var services = local.Services(mode);
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
            using var schema = ModelSchema(root);
            Assert.Equal(count, schema.RootElement.GetProperty("properties").GetProperty("questions").GetProperty("minItems").GetInt32());
            if (count == 2) Assert.Equal(Source, input.RootElement.GetProperty("materials")[0].GetProperty("body").GetString());
        }
    }

    [Theory]
    [InlineData("json_schema")]
    [InlineData("json_object")]
    public async Task Output_schema_UTF8_limit_is_inclusive_in_every_mode(string mode)
    {
        await using var local = await LocalAiProvider.StartAsync();
        using var services = local.Services(mode);
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

    // Strict mode sends the schema only natively; the other modes can only carry it in the prompt.
    private static JsonDocument ModelSchema(JsonElement request) =>
        JsonDocument.Parse(request.GetProperty("response_format").TryGetProperty("json_schema", out var format)
            ? format.GetProperty("schema").GetRawText()
            : request.GetProperty("messages")[0].GetProperty("content").GetString()!.Split("\nOutput JSON schema:\n")[1]);

    [Theory]
    [InlineData(32000, true)]
    [InlineData(32001, false)]
    public async Task Response_character_limit_is_inclusive(int length, bool accepted)
    {
        var json = """{"result":{"proposal":null,"clarification":"איזה גיל?"},"assumptions":[]}""";
        using var chat = new AiFixtures.ScriptedChat(json.PadRight(length));
        using var service = Service(chat);
        if (accepted) await service.AuthorAsync(new FamilyLearning.Api.TaskEngine.Models.TemplateAuthoringInput("רעיון"), default);
        else await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new FamilyLearning.Api.TaskEngine.Models.TemplateAuthoringInput("רעיון"), default));
    }
}
