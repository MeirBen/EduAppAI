using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class RequestValidationTests
{
    [Fact]
    public async Task Published_limits_are_the_ones_validation_enforces()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var limits = await parent.GetFromJsonAsync<JsonElement>("/api/limits");
        var maxQuestions = limits.GetProperty("maxQuestionCount").GetInt32();
        var nameLength = limits.GetProperty("nameLength").GetInt32();
        Assert.Empty(LearningPlanValidator.Validate(Numeric(maxQuestions) with { Name = new string('א', nameLength) }));
        Assert.Contains("defaults.questionCount", LearningPlanValidator.Validate(Numeric(maxQuestions + 1)).Keys);
        Assert.Contains("name", LearningPlanValidator.Validate(Numeric() with { Name = new string('א', nameLength + 1) }).Keys);
    }

    [Theory]
    [InlineData("document")]
    public async Task Null_save_members_return_validation_errors_without_changing_the_draft(string member)
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ActivityDraftTests.Create(parent, Numeric(1));
        var edit = ActivityDraftTests.Edit(draft);
        edit[member] = null;
        using var response = await parent.PutAsJsonAsync(ActivityDraftTests.Path(draft), edit);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsObject());
        Assert.True(JsonNode.DeepEquals(draft, await parent.GetFromJsonAsync<JsonNode>(ActivityDraftTests.Path(draft))));
    }

    [Theory]
    [InlineData("controlValues", "null")]
    [InlineData("choiceCount", "null")]
    [InlineData("controlValues", "{\"22222222222222222222222222222222\":null}")]
    [InlineData("controlValues", "{\"33333333333333333333333333333333\":\"0\"}")]
    public async Task Activity_HTTP_preserves_explicit_invalid_values_for_validation(string member, string json)
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var plan = PresencePlan();
        var body = JsonSerializer.SerializeToNode(new { plan, input = new TaskRequest(plan.Defaults) }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body["input"]![member] = JsonNode.Parse(json);
        await AssertBadRequestAsync(parent, "/api/activity-drafts", body.ToJsonString());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/activity-drafts")).GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activity_HTTP_round_trips_omission_false_zero_and_empty_without_coercion(bool supplied)
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var plan = PresencePlan();
        var body = JsonSerializer.SerializeToNode(new { plan, input = new TaskRequest(plan.Defaults) }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (supplied) body["input"]!["controlValues"] = JsonNode.Parse("""{"22222222222222222222222222222222":false,"33333333333333333333333333333333":0,"44444444444444444444444444444444":""}""");
        using var response = await parent.PostAsJsonAsync("/api/activity-drafts", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True(JsonNode.DeepEquals(body["input"], draft["input"]));
        var edit = ActivityDraftTests.Edit(draft);
        var saved = await ActivityDraftTests.Save(parent, draft, edit);
        Assert.True(JsonNode.DeepEquals(body["input"], saved["input"]));
    }

    private static LearningPlan PresencePlan() => Numeric(1) with
    {
        Controls = [new(ControlId, "דגל", "boolean", "אפשרות", Default: Json("true")),
            new(OtherId, "ערך", "integer", "מספר", Default: Json("5")),
            new("44444444444444444444444444444444", "טקסט", "text", "טקסט נוסף", Default: Json("\"ברירה\""), MaxLength: 100)]
    };

    [Fact]
    public async Task Missing_or_null_template_members_remain_client_errors()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        foreach (var member in new[] { "schemaVersion", "name", "goal", "guidance", "defaults", "materials", "questions", "controls" })
        {
            var definition = AiFixtures.PlanJson().AsObject();
            definition.Remove(member);
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
            definition[member] = null;
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
        }
        foreach (var member in new[] { "topic", "audience", "difficulty", "questionCount" })
        {
            var definition = AiFixtures.PlanJson();
            var defaults = definition["defaults"]!.AsObject();
            defaults.Remove(member);
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
            defaults[member] = null;
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
        }
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
    }

    [Fact]
    public async Task Missing_or_null_version_members_do_not_change_data()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var created = await parent.PostAsJsonAsync("/api/templates", AiFixtures.PlanJson());
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        foreach (var body in new[] { "{}", """{"expectedVersion":1}""", """{"expectedVersion":1,"definition":null}""" })
            await AssertBadRequestAsync(parent, $"/api/templates/{id}/versions", body);
        var missingExpectedVersion = new JsonObject
        {
            ["definition"] = AiFixtures.PlanJson()
        };
        await AssertBadRequestAsync(parent, $"/api/templates/{id}/versions", missingExpectedVersion.ToJsonString());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
        var unchanged = await parent.GetFromJsonAsync<JsonElement>($"/api/templates/{id}");
        Assert.Equal(1, unchanged.GetProperty("currentVersion").GetInt32());
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("questionCount")]
    public async Task Missing_or_null_task_input_is_rejected_without_creating_a_draft(string member)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var input = JsonSerializer.SerializeToNode(new TaskRequest(Numeric().Defaults), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var section = (member == "settings" ? input : input["settings"]!).AsObject();
        section.Remove(member);
        await AssertBadRequestAsync(parent, "/api/activity-drafts", new JsonObject { ["plan"] = AiFixtures.PlanJson(), ["input"] = input.DeepClone() }.ToJsonString());
        section[member] = null;
        await AssertBadRequestAsync(parent, "/api/activity-drafts", new JsonObject { ["plan"] = AiFixtures.PlanJson(), ["input"] = input.DeepClone() }.ToJsonString());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
    }

    private static async Task AssertBadRequestAsync(HttpClient client, string path, string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, body);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"{path} accepted invalid input {json}: {(int)response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
