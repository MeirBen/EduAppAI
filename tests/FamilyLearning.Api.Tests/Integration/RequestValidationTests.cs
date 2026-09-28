using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class RequestValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Missing_or_null_template_members_remain_client_errors()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        foreach (var member in new[] { "schemaVersion", "name", "instanceParameters", "generation" })
        {
            var definition = JsonSerializer.SerializeToNode(AiAuthoringTests.Definition(), JsonOptions)!.AsObject();
            definition.Remove(member);
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
            definition[member] = null;
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
        }
        foreach (var member in new[] { "instructions" })
        {
            var definition = JsonSerializer.SerializeToNode(AiAuthoringTests.Definition(), JsonOptions)!;
            definition["generation"]![member] = null;
            await AssertBadRequestAsync(parent, "/api/templates", definition.ToJsonString());
        }
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
    }

    [Fact]
    public async Task Missing_or_null_instance_and_version_members_do_not_change_data()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var created = await parent.PostAsJsonAsync("/api/templates", AiAuthoringTests.Definition());
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        foreach (var body in new[] { "{}", """{"parameters":null}""" })
            await AssertBadRequestAsync(parent, $"/api/templates/{id}/instances", body);
        foreach (var body in new[] { "{}", """{"expectedVersion":1}""", """{"expectedVersion":1,"definition":null}""" })
            await AssertBadRequestAsync(parent, $"/api/templates/{id}/versions", body);
        var missingExpectedVersion = new JsonObject
        {
            ["definition"] = JsonSerializer.SerializeToNode(AiAuthoringTests.Definition(), JsonOptions)
        };
        await AssertBadRequestAsync(parent, $"/api/templates/{id}/versions", missingExpectedVersion.ToJsonString());
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
        var unchanged = await parent.GetFromJsonAsync<JsonElement>($"/api/templates/{id}");
        Assert.Equal(1, unchanged.GetProperty("currentVersion").GetInt32());
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
