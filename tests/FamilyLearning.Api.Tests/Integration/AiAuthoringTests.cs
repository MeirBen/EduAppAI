using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Fixtures.AiFixtures;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class AiAuthoringTests
{
    [Fact]
    public async Task Plan_chat_returns_correlated_clarifications_then_a_proposal_without_publishing()
    {
        var plan = Numeric();
        var clarification = """{"result":{"proposal":null,"clarification":"לאיזה גיל?"},"assumptions":[]}""";
        var proposal = JsonSerializer.Serialize(new { result = new { proposal = plan, clarification = (string?)null }, assumptions = Array.Empty<string>() }, EngineJson.Options);
        await using var app = new GenerationHarness(clarification, clarification, proposal);
        using var parent = await app.ParentAsync();
        AuthoringTurn[] context = [];
        foreach (var message in new[] { "תרגול חשבון", "כיתה ג", "מספרים קטנים" })
        {
            var requestId = Guid.NewGuid().ToString();
            using var response = await parent.PostAsJsonAsync("/api/ai/template-drafts", new TemplateAuthoringInput(message, Context: context, RequestId: requestId, BaseRevision: 7));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var reply = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.Equal(requestId, reply["requestId"]!.GetValue<string>());
            Assert.Equal(7, reply["baseRevision"]!.GetValue<int>());
            Assert.NotNull(reply["generationMetadata"]);
            if (reply["clarification"] is { } question)
                context = [.. context, new("parent", message), new("assistant", question.GetValue<string>())];
            else
            {
                Assert.Equal("מספרים", reply["proposal"]!["name"]!.GetValue<string>());
                Assert.Equal("added", reply["changes"]![0]!["kind"]!.GetValue<string>());
            }
        }
        Assert.Equal(3, app.Chat.Requests.Count);
        Assert.Contains("תרגול חשבון", app.Chat.Requests[2].Input);
        Assert.DoesNotContain("requestId", app.Chat.Requests[2].Input);
        Assert.DoesNotContain("baseRevision", app.Chat.Requests[2].Input);
        Assert.Empty(await parent.GetFromJsonAsync<JsonElement[]>("/api/templates") ?? []);
        var status = await parent.GetFromJsonAsync<JsonElement>("/api/ai/status");
        Assert.Equal(EngineVersions.SchemaVersion, status.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task Plan_chat_rejects_changed_fixed_source_and_reports_real_removals()
    {
        var plan = Supplied();
        string Reply(LearningPlan value) => JsonSerializer.Serialize(new { result = new { proposal = value, clarification = (string?)null }, assumptions = Array.Empty<string>() }, EngineJson.Options);
        await using var app = new GenerationHarness(Reply(plan with { Materials = [plan.Materials[0] with { Text = "changed" }] }), Reply(plan with { Materials = [] }));
        using var parent = await app.ParentAsync();
        using var rejected = await parent.PostAsJsonAsync("/api/ai/template-drafts", new TemplateAuthoringInput("לשנות", plan));
        Assert.Equal(HttpStatusCode.BadGateway, rejected.StatusCode);
        using var removed = await parent.PostAsJsonAsync("/api/ai/template-drafts", new TemplateAuthoringInput("להסיר", plan));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var reply = (await removed.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("removed", reply["changes"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal(MaterialId, reply["changes"]![0]!["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(6, 2001)]
    public async Task Plan_chat_rejects_oversized_unresolved_context_before_the_provider(int turns, int length)
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var context = Enumerable.Range(0, turns).Select(_ => new AuthoringTurn("parent", new string('x', length))).ToArray();
        using var response = await parent.PostAsJsonAsync("/api/ai/template-drafts", new TemplateAuthoringInput("עוד", Context: context));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Chat.Requests);
    }

}
