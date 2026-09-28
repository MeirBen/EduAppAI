using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using static FamilyLearning.Api.Tests.Fixtures.AiFixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class AiAuthoringTests
{
    [Fact]
    public async Task Prompt_creates_only_a_draft_then_one_template_generates_distinct_frozen_instances()
    {
        var chat = new ScriptedChat(Definition().ToJsonString(), Content().ToJsonString(), Content("חלל", 3).ToJsonString());
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        using var draftResponse = await parent.PostAsJsonAsync("/api/ai/template-drafts", new { prompt = "תבנית הבנת הנקרא עם נושא ומספר שאלות לבחירה" });
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("template-authoring-v4", draft.GetProperty("generationMetadata").GetProperty("promptVersion").GetString());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
        var definition = draft.GetProperty("definition");
        Assert.False(string.IsNullOrWhiteSpace(definition.GetProperty("generation").GetProperty("instructions").GetString()));
        using var save = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.Created, save.StatusCode);
        var template = await save.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        using var first = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var frozen = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var second = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { theme = "חלל", count = 3 } });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var next = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, next.GetProperty("content").GetProperty("questions").GetArrayLength());
        Assert.Equal("חלל", next.GetProperty("content").GetProperty("title").GetString());
        var loaded = await parent.GetFromJsonAsync<JsonElement>($"/api/instances/{frozen.GetProperty("id").GetGuid()}");
        Assert.Equal(frozen.GetProperty("content").GetRawText(), loaded.GetProperty("content").GetRawText());
        Assert.Equal("test-free-model", loaded.GetProperty("generationMetadata").GetProperty("model").GetString());
        Assert.Equal("instance-generation-v4", loaded.GetProperty("generationMetadata").GetProperty("promptVersion").GetString());
        Assert.Equal(3, chat.Requests.Count);
        Assert.All(chat.Requests, request =>
        {
            var format = Assert.IsType<ChatResponseFormatJson>(request.Options!.ResponseFormat);
            Assert.Contains(JsonSerializer.Serialize(format.Schema), request.Input);
            Assert.Null(request.Options.Tools);
            Assert.Equal(8192, request.Options.MaxOutputTokens);
            Assert.DoesNotContain("@example.test", request.Input);
            Assert.DoesNotContain("familyId", request.Input);
        });
        Assert.Contains("חלל", chat.Requests[2].Input);
        using var stranger = await app.ParentAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } })).StatusCode);
        Assert.Equal(3, chat.Requests.Count);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("wrong-count")]
    [InlineData("invalid-answer")]
    [InlineData("truncated")]
    [InlineData("provider-error")]
    [InlineData("timeout")]
    public async Task Invalid_provider_output_never_saves_an_instance(string scenario)
    {
        var content = Content();
        if (scenario == "wrong-count") content = Content(count: 1);
        if (scenario == "invalid-answer") content["questions"]![0]!["answer"]!["value"] = "";
        var chat = new ScriptedChat(scenario == "invalid-json" ? "not JSON" : content.ToJsonString())
        {
            FinishReason = scenario == "truncated" ? ChatFinishReason.Length : ChatFinishReason.Stop,
            Fail = scenario == "provider-error",
            WaitForCancellation = scenario == "timeout"
        };
        using var app = new ApiFactory(services =>
        {
            services.AddSingleton<IChatClient>(chat);
            services.Configure<AiGenerationOptions>(options => options.RequestTimeoutSeconds = 1);
        });
        using var parent = await app.ParentAsync();
        using var saved = await parent.PostAsJsonAsync("/api/templates", Definition());
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var response = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });
        Assert.Equal(scenario == "timeout" ? HttpStatusCode.GatewayTimeout : HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("provider secret", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
    }

    [Fact]
    public async Task Unconfigured_ai_is_explicit_and_does_not_prevent_blueprint_storage()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var status = await parent.GetFromJsonAsync<JsonElement>("/api/ai/status");
        Assert.False(status.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await parent.PostAsJsonAsync("/api/ai/template-drafts", new { prompt = "מדעים" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await parent.PostAsJsonAsync("/api/templates", Definition())).StatusCode);
    }

    [Fact]
    public async Task Authoring_rejects_invented_top_level_fields_without_saving_a_template()
    {
        var proposal = Definition();
        proposal["labels"] = new System.Text.Json.Nodes.JsonObject { ["topicLabel"] = "נושא" };
        var chat = new ScriptedChat(proposal.ToJsonString());
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();

        using var response = await parent.PostAsJsonAsync("/api/ai/template-drafts", new { prompt = "תבנית ללמידה" });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("missing-instructions")]
    [InlineData("bad-count-binding")]
    [InlineData("math-settings")]
    public async Task Rejects_invalid_ai_blueprints(string scenario)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = Definition();
        var generation = definition["generation"]!;
        if (scenario == "missing-instructions") generation["instructions"] = " ";
        if (scenario == "bad-count-binding") generation["questionCountParameter"] = "theme";
        if (scenario == "math-settings") generation["generator"] = "math-v1";
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/templates", definition)).StatusCode);
    }

}
