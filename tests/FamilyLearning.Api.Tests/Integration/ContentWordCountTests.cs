using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using static FamilyLearning.Api.Tests.Fixtures.AiFixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ContentWordCountTests
{
    [Theory]
    [InlineData(100, 150, 54, false)]
    [InlineData(100, 150, 56, false)]
    [InlineData(100, 150, 79, false)]
    [InlineData(100, 150, 100, true)]
    [InlineData(100, 150, 150, true)]
    [InlineData(100, 150, 151, false)]
    [InlineData(100, null, 100, true)]
    [InlineData(null, 150, 151, false)]
    [InlineData(null, null, 0, true)]
    public async Task Declared_word_limits_guard_persistence_with_one_call(int? min, int? max, int words, bool accepted)
    {
        var definition = Definition();
        if (min.HasValue || max.HasValue)
            definition["generation"]!["contentWordCount"] = new JsonObject { ["min"] = min, ["max"] = max };
        var content = Content();
        content["contentBlocks"] = words == 0 ? new JsonArray() : new JsonArray(new JsonObject
        {
            ["type"] = "text",
            ["text"] = string.Join(' ', Enumerable.Repeat("מילה", words))
        });
        var chat = new ScriptedChat(content.ToJsonString());
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        using var saved = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var response = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });

        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(chat.Requests);
        Assert.Equal(accepted ? 1 : 0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
        if (!accepted)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("urn:family-learning:ai-validation", problem.GetProperty("type").GetString());
            var message = problem.GetProperty("errors").GetProperty("contentBlocks.wordCount")[0].GetString();
            Assert.Contains(words.ToString(), message);
        }
    }
}
