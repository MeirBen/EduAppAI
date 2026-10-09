using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.ActivityRevisionTests;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityRevisionPipelineTests
{
    [Theory]
    [InlineData(1000, true)]
    [InlineData(3900, false)]
    public async Task Pending_rewrites_defer_capacity_until_all_materials_are_final(int secondLength, bool succeeds)
    {
        var ids = new[] { MaterialId, OtherId, new string('4', 32) };
        var plan = Reading() with
        {
            Settings = Numeric(1).Settings,
            Materials = ids.Select(id => Reading().Materials[0] with { Id = id, Length = null }).ToArray()
        };
        var change = Change(plan) with { MaterialEdits = [new(ids[0], "הרחב"), new(ids[1], "קצר")] };
        await using var app = new GenerationHarness(
            Serialize(new { result = new RevisionDecision(null, null, change) }),
            Serialize(new MaterialCandidate(ids[0], null, new string('א', 3900))),
            Serialize(new MaterialCandidate(ids[1], null, new string('ב', secondLength))),
            GenerationHarness.Questions("text-input"));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"] = JsonSerializer.SerializeToNode(new
        {
            title = "לפני",
            instructions = (string?)null,
            materials = ids.Select((id, i) => new { id, title = (string?)null, body = new string('ג', new[] { 1000, 3900, 2000 }[i]) }),
            questions = new[] { new { id = (string?)null, prompt = "שאלה", interaction = new { type = "text-input" }, answer = new { value = "תשובה" }, points = 1 } }
        });
        draft = await Seed(parent, draft, edit);
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", new
        { operationKey = Guid.NewGuid(), expectedRevision = 2, kind = "Revise", message = "הרחב את הראשון וקצר את השני" });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var operation = (await start.Content.ReadFromJsonAsync<JsonNode>())!;
        while (await app.Worker.RunNextAsync(default)) { }
        var state = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal(succeeds ? "completed" : "failed", state["status"]!.GetValue<string>());
        Assert.Equal(succeeds ? 4 : 3, app.Chat.Requests.Count);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        if (!succeeds) Assert.True(JsonNode.DeepEquals(draft["document"], saved["document"]));
        else
        {
            Assert.Equal(3, saved["revision"]!.GetValue<long>());
            Assert.Equal(1000, saved["document"]!["materials"]![1]!["body"]!.GetValue<string>().Length);
            Assert.Empty(saved["diagnostics"]!.AsObject());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0, true)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Eight_stage_revision_applies_once_and_failure_at_every_stage_preserves_the_base(int failureStage, bool largeRewrite = false)
    {
        var ids = new[] { MaterialId, OtherId, new string('4', 32) };
        var plan = Reading() with
        {
            Settings = Numeric(1).Settings,
            Materials = ids.Select(id => Reading().Materials[0] with { Id = id, Length = null }).ToArray()
        };
        var proposed = plan with { Guidance = "דרישה חדשה", Materials = [.. plan.Materials, plan.Materials[0] with { Id = null }] };
        var calls = 0;
        await using var app = new GenerationHarness
        {
            Chat = new AiFixtures.ScriptedChat
            {
                Respond = text =>
                {
                    calls++;
                    if (calls == failureStage) return "{}";
                    var payload = JsonNode.Parse(text.Split('\n')[^1])!;
                    if (calls == 1) return Serialize(new { result = new RevisionDecision(null, null, Change(proposed)) });
                    if (calls is >= 2 and <= 4)
                        return Serialize(new MaterialCandidate(payload["target"]!["id"]!.GetValue<string>(), null, largeRewrite ? new string('א', 2500) : "טקסט מעודכן " + calls));
                    if (calls == 5) return GenerationHarness.Ideas;
                    if (calls == 6) return Serialize(new MaterialCandidateBatch([new(payload["targetIds"]![0]!.GetValue<string>(), null, "טקסט חדש")]));
                    if (calls == 7) return new JsonObject { ["materials"] = payload["materials"]!.DeepClone() }.ToJsonString();
                    return GenerationHarness.Questions("text-input");
                }
            }
        };
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"] = JsonSerializer.SerializeToNode(new
        {
            title = "לפני",
            instructions = "הוראות קיימות",
            materials = ids.Select(id => new { id, title = (string?)null, body = "טקסט קודם" }).ToArray(),
            questions = new[] { new { id = (string?)null, prompt = largeRewrite ? new string('ש', 500) : "שאלה קודמת", interaction = new { type = "text-input" }, answer = new { value = largeRewrite ? new string('ק', 200) : "old-private-key" }, points = 1 } }
        });
        draft = await Seed(parent, draft, edit);
        using var started = await parent.PostAsJsonAsync(Path(draft) + "/operations",
            new { operationKey = Guid.NewGuid(), expectedRevision = 2, kind = "Revise", message = "שנה את ההנחיה והוסף טקסט" });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var operation = (await started.Content.ReadFromJsonAsync<JsonNode>())!;
        var expectedCalls = failureStage == 0 ? 8 : failureStage;
        for (var i = 1; i <= expectedCalls; i++)
        {
            Assert.True(await app.Worker.RunNextAsync(default));
            var stored = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
            if (i < 8 || failureStage != 0)
            {
                Assert.Equal(2, stored["revision"]!.GetValue<long>());
                Assert.True(JsonNode.DeepEquals(draft["document"], stored["document"]));
                Assert.True(JsonNode.DeepEquals(draft["plan"], stored["plan"]));
            }
        }
        Assert.False(await app.Worker.RunNextAsync(default));
        var state = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal(failureStage == 0 ? "completed" : "failed", state["status"]!.GetValue<string>());
        Assert.Equal(expectedCalls, app.Chat.Requests.Count);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(2, saved["chat"]!.AsArray().Count);
        if (failureStage != 0) return;
        Assert.Equal(3, saved["revision"]!.GetValue<long>());
        Assert.True(saved["canUndo"]!.GetValue<bool>());
        Assert.Equal(4, saved["document"]!["materials"]!.AsArray().Count);
        Assert.Empty(saved["diagnostics"]!.AsObject());
        if (largeRewrite) return;
        var payloads = app.Chat.Requests.Select(r => JsonNode.Parse(r.Input.Split('\n')[^1])!).ToArray();
        Assert.Contains("old-private-key", payloads[0].ToJsonString());
        Assert.All(payloads.Skip(1), payload =>
        {
            var json = payload.ToJsonString();
            Assert.DoesNotContain("old-private-key", json);
            Assert.DoesNotContain("acceptance", json);
            Assert.DoesNotContain("origin", json);
            Assert.DoesNotContain("operationId", json);
            Assert.Null(payload["context"]);
        });
        Assert.Contains(payloads[1]["materials"]!.AsArray(), m => m!["state"]!.GetValue<string>() == "pending");
        Assert.Contains(payloads[2]["materials"]!.AsArray(), m => m!["body"]!.GetValue<string>() == "טקסט מעודכן 2" && m["state"]!.GetValue<string>() == "final");
        Assert.Single(payloads[5]["targetIds"]!.AsArray());
        Assert.Single(payloads[6]["materials"]!.AsArray());
        Assert.Equal(3, payloads[6]["retained"]!.AsArray().Count);
        Assert.Equal(4, payloads[7]["materials"]!.AsArray().Count);
        Assert.NotNull(payloads[7]["previous"]!["questions"]![0]!["id"]);
        Assert.Null(payloads[7]["previous"]!["questions"]![0]!["answer"]);
    }
}
