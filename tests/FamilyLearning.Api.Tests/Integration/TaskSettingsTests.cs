using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using static FamilyLearning.Api.Tests.Fixtures.AiFixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class TaskSettingsTests
{
    private static JsonNode Blueprint() => JsonNode.Parse("""
        {"schemaVersion":4,"name":"תרגול","instanceParameters":[],"generation":{
          "instructions":"יש ליצור שאלות קצרות בהתאם להגדרות המשימה.",
          "defaults":{"topic":"בעלי חיים","audience":"כיתה ג׳","difficulty":"easy","questionCount":2}}}
        """)!;

    [Fact]
    public async Task Shared_settings_are_forwarded_and_saved_independently_of_template_defaults()
    {
        using var chat = new ScriptedChat(Content("חלל", 3).ToJsonString());
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        using var published = await parent.PostAsJsonAsync("/api/templates", Blueprint());
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var id = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var input = new { settings = new { topic = "חלל", audience = "לומדים מבוגרים", difficulty = "hard", questionCount = 3 }, parameters = new { } };
        using var response = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        var settings = saved.GetProperty("input").GetProperty("settings");
        Assert.Equal("חלל", settings.GetProperty("topic").GetString());
        Assert.Equal("לומדים מבוגרים", settings.GetProperty("audience").GetString());
        Assert.Equal("hard", settings.GetProperty("difficulty").GetString());
        Assert.Equal(3, settings.GetProperty("questionCount").GetInt32());
        Assert.Equal(3, saved.GetProperty("content").GetProperty("questions").GetArrayLength());
        Assert.Contains("\"topic\":\"חלל\"", Assert.Single(chat.Requests).Input);
        Assert.Contains("\"difficulty\":\"hard\"", chat.Requests[0].Input);
        Assert.DoesNotContain("בעלי חיים", chat.Requests[0].Input);
        var changed = Blueprint();
        changed["generation"]!["defaults"]!["topic"] = "צמחים";
        using var revision = await parent.PostAsJsonAsync($"/api/templates/{id}/versions", new { expectedVersion = 1, definition = changed });
        Assert.Equal(HttpStatusCode.Created, revision.StatusCode);
        var loaded = await parent.GetFromJsonAsync<JsonElement>($"/api/instances/{saved.GetProperty("id").GetGuid()}");
        Assert.Equal(saved.GetProperty("input").GetRawText(), loaded.GetProperty("input").GetRawText());
        Assert.Single(chat.Requests);
    }

    [Theory]
    [InlineData("topic", "\" \"")]
    [InlineData("audience", "null")]
    [InlineData("difficulty", "\"invented\"")]
    [InlineData("questionCount", "0")]
    [InlineData("questionCount", "1.5")]
    public async Task Invalid_shared_settings_never_call_ai(string field, string value)
    {
        using var chat = new ScriptedChat();
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        using var published = await parent.PostAsJsonAsync("/api/templates", Blueprint());
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var id = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var settings = Blueprint()["generation"]!["defaults"]!.DeepClone();
        settings[field] = JsonNode.Parse(value);
        using var response = await parent.PostAsJsonAsync($"/api/templates/{id}/instances",
            new JsonObject { ["settings"] = settings, ["parameters"] = new JsonObject() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(chat.Requests);
        var invalidDefaults = Blueprint();
        invalidDefaults["generation"]!["defaults"]![field] = JsonNode.Parse(value);
        using var invalidTemplate = await parent.PostAsJsonAsync("/api/templates", invalidDefaults);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTemplate.StatusCode);
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("audience")]
    public async Task Shared_text_accepts_the_length_boundary_and_rejects_excess(string field)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = Blueprint();
        definition["generation"]!["defaults"]![field] = new string('א', 200);
        using var valid = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        definition["generation"]!["defaults"]![field] = new string('א', 201);
        using var invalid = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("audience")]
    [InlineData("difficulty")]
    [InlineData("questionCount")]
    public async Task Dynamic_parameters_cannot_redefine_shared_settings(string key)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = Blueprint();
        definition["instanceParameters"]!.AsArray().Add(new JsonObject { ["key"] = key, ["label"] = "כפילות", ["type"] = "text" });
        using var response = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("instanceParameters[0]", out _));
    }
}
