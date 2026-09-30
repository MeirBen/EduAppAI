using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using static FamilyLearning.Api.Tests.Fixtures.AiFixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class TemplateLengthGuidanceTests
{
    [Fact]
    public async Task Length_guidance_reaches_generation_without_rejecting_valid_short_content()
    {
        var definition = Definition();
        const string instructions = "יש ליצור קטע בן 100–150 מילים בנושא \"sourceText\".";
        definition["generation"]!["instructions"] = instructions;
        var content = Content();
        using var chat = new ScriptedChat(content.ToJsonString());
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        using var saved = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var template = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(instructions, template.GetProperty("definition").GetProperty("generation").GetProperty("instructions").GetString());
        var id = template.GetProperty("id").GetGuid();

        using var created = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", Input());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Single(chat.Requests);
        Assert.Contains(JsonSerializer.Serialize(instructions, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), chat.Requests[0].Input);
        Assert.Single((await parent.GetFromJsonAsync<JsonElement>("/api/instances")).EnumerateArray());
        var task = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(content["contentBlocks"]![0]!["text"]!.GetValue<string>(),
            task.GetProperty("content").GetProperty("contentBlocks")[0].GetProperty("text").GetString());
    }
}
