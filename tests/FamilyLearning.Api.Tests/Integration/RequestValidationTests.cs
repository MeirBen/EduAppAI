using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Tests.Fixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class RequestValidationTests
{
    [Fact]
    public async Task Missing_or_null_template_members_remain_client_errors()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        foreach (var member in new[] { "schemaVersion", "name", "instanceParameters", "generation" })
        {
            var definition = AiFixtures.Definition().AsObject();
            definition.Remove(member);
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
            definition[member] = null;
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
        }
        foreach (var member in new[] { "instructions", "defaults" })
        {
            var definition = AiFixtures.Definition();
            definition["generation"]!.AsObject().Remove(member);
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
            definition["generation"]![member] = null;
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
        }
        foreach (var member in new[] { "topic", "audience", "difficulty", "questionCount" })
        {
            var definition = AiFixtures.Definition();
            var defaults = definition["generation"]!["defaults"]!.AsObject();
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
        var created = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        foreach (var body in new[] { "{}", """{"expectedVersion":1}""", """{"expectedVersion":1,"definition":null}""" })
            await AssertBadRequestAsync(parent, $"/api/templates/{id}/versions", body);
        var missingExpectedVersion = new JsonObject
        {
            ["definition"] = AiFixtures.Definition()
        };
        await AssertBadRequestAsync(parent, $"/api/templates/{id}/versions", missingExpectedVersion.ToJsonString());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
        var unchanged = await parent.GetFromJsonAsync<JsonElement>($"/api/templates/{id}");
        Assert.Equal(1, unchanged.GetProperty("currentVersion").GetInt32());
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("parameters")]
    [InlineData("topic")]
    [InlineData("audience")]
    [InlineData("difficulty")]
    [InlineData("questionCount")]
    public async Task Missing_or_null_task_input_is_rejected_without_creating_a_draft(string member)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        using var created = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var input = JsonSerializer.SerializeToNode(AiFixtures.Input(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var section = (member is "settings" or "parameters" ? input : input["settings"]!).AsObject();
        section.Remove(member);
        await AssertBadRequestAsync(parent, $"/api/templates/{id}/instances", input.ToJsonString());
        section[member] = null;
        await AssertBadRequestAsync(parent, $"/api/templates/{id}/instances", input.ToJsonString());
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
