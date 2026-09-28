using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Tests.TaskEngine;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class StaticAuthoringTests
{
    internal static JsonObject Definition() => JsonNode.Parse("""
        {
          "schemaVersion": 1, "name": "קריאה וחשיבה", "instanceParameters": [],
          "generation": {
            "mode": "static",
            "content": {
              "title": "קריאה וחשיבה", "instructions": "קוראים ועונים.",
              "contentBlocks": [{"type":"text","text":"נועה מצאה 3 ספרים."}],
              "questions": [
                {"id":"number","prompt":"כמה ספרים?","interaction":{"type":"numeric-input"},"answer":{"value":"3"},"points":2},
                {"id":"name","prompt":"מי מצאה?","interaction":{"type":"text-input"},"answer":{"value":"נועה"},"points":1},
                {"id":"choice","prompt":"מה מצאה?","interaction":{"type":"single-choice","options":["ספרים","פרחים"]},"answer":{"value":"ספרים"},"points":3}
              ]
            }
          }
        }
        """)!.AsObject();

    [Fact]
    public async Task Creates_mixed_static_draft_and_preserves_it_after_revision()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = Definition();
        using var created = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        using var generated = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var draft = await generated.Content.ReadFromJsonAsync<JsonElement>();
        var content = draft.GetProperty("content");
        Assert.Equal("נועה מצאה 3 ספרים.", content.GetProperty("contentBlocks")[0].GetProperty("text").GetString());
        Assert.Equal("ספרים", content.GetProperty("questions")[2].GetProperty("answer").GetProperty("value").GetString());
        Assert.Equal(3, content.GetProperty("questions").GetArrayLength());
        definition["generation"]!["content"]!["contentBlocks"]![0]!["text"] = "קטע חדש";
        using var published = await parent.PostAsJsonAsync($"/api/templates/{id}/versions", new { expectedVersion = 1, definition });
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var old = await parent.GetFromJsonAsync<JsonElement>($"/api/instances/{draft.GetProperty("id").GetGuid()}");
        Assert.Equal(content.GetRawText(), old.GetProperty("content").GetRawText());
        Assert.Equal(1, old.GetProperty("templateVersion").GetInt32());
        using var next = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });
        var newer = await next.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("קטע חדש", newer.GetProperty("content").GetProperty("contentBlocks")[0].GetProperty("text").GetString());
        Assert.Equal(2, newer.GetProperty("templateVersion").GetInt32());

        using var stranger = await app.ParentAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/instances/{draft.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/templates/{id}/versions", new { expectedVersion = 2, definition })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { difficulty = "easy" } })).StatusCode);
    }

    [Theory]
    [InlineData("missing-points")]
    [InlineData("null-question")]
    [InlineData("duplicate-id")]
    [InlineData("unsupported-interaction")]
    [InlineData("missing-choice-answer")]
    [InlineData("duplicate-choice")]
    [InlineData("multiline-choice")]
    [InlineData("padded-choice")]
    [InlineData("null-choice")]
    [InlineData("numeric-exponent")]
    [InlineData("numeric-overflow")]
    [InlineData("negative-points")]
    [InlineData("fractional-points")]
    [InlineData("null-content")]
    [InlineData("unknown-block")]
    [InlineData("unused-parameters")]
    [InlineData("contradictory-settings")]
    [InlineData("too-many-questions")]
    [InlineData("oversize-total")]
    public async Task Rejects_invalid_authored_content_without_writing(string scenario)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = Definition();
        var content = definition["generation"]!["content"]!;
        var questions = content["questions"]!.AsArray();
        switch (scenario)
        {
            case "missing-points": questions[0]!.AsObject().Remove("points"); break;
            case "null-question": questions[0] = null; break;
            case "duplicate-id": questions[1]!["id"] = "number"; break;
            case "unsupported-interaction": questions[0]!["interaction"]!["type"] = "html"; break;
            case "missing-choice-answer": questions[2]!["answer"]!["value"] = "missing"; break;
            case "duplicate-choice": questions[2]!["interaction"]!["options"]![1] = "ספרים"; break;
            case "multiline-choice": questions[2]!["interaction"]!["options"]![1] = "פרחים\nעצים"; break;
            case "padded-choice": questions[2]!["interaction"]!["options"]![1] = " פרחים "; break;
            case "null-choice": questions[2]!["interaction"]!["options"]![0] = null; break;
            case "numeric-exponent": questions[0]!["answer"]!["value"] = "1e3"; break;
            case "numeric-overflow": questions[0]!["answer"]!["value"] = new string('9', 100); break;
            case "negative-points": questions[0]!["points"] = -1; break;
            case "fractional-points": questions[0]!["points"] = 1.5; break;
            case "null-content": definition["generation"]!["content"] = null; break;
            case "unknown-block": content["contentBlocks"]![0]!["type"] = "html"; break;
            case "unused-parameters": definition["instanceParameters"]!.AsArray().Add(new JsonObject { ["key"] = "unused", ["label"] = "Unused", ["type"] = "text" }); break;
            case "contradictory-settings": definition["generation"]!["generator"] = "math-v1"; break;
            case "too-many-questions":
                while (questions.Count < 21)
                {
                    var question = questions[0]!.DeepClone();
                    question["id"] = $"q{questions.Count}";
                    questions.Add(question);
                }
                break;
            case "oversize-total": content["contentBlocks"] = new JsonArray(Enumerable.Range(0, 3).Select(_ => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = new string('a', 3000) }).ToArray()); break;
        }
        using var response = await parent.PostAsJsonAsync("/api/templates", definition);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
    }

    [Fact]
    public async Task Rejects_static_content_on_a_math_template()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = JsonSerializer.SerializeToNode(MathGenerationTests.Definition(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        definition["generation"]!["content"] = Definition()["generation"]!["content"]!.DeepClone();
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/templates", definition)).StatusCode);
    }
}
