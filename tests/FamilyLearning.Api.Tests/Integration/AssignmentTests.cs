using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class AssignmentTests
{
    internal static async Task<Guid> Snapshot(HttpClient parent)
    {
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        using var release = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        return (await release.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
    }

    internal static async Task<JsonNode> Assign(HttpClient parent, JsonNode child, Guid snapshotId)
    {
        using var response = await parent.PostAsJsonAsync("/api/assignments", new { childId = child["id"]!.GetValue<Guid>(), snapshotId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Theory]
    [InlineData("awaiting-review")]
    [InlineData("completed")]
    public async Task Submitted_work_is_filtered_separately_and_cannot_be_withdrawn_or_reopened(string status)
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var snapshot = await Snapshot(parent);
        var assignment = await Assign(parent, profile, snapshot);
        var id = assignment["id"]!.GetValue<Guid>();
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            await db.Assignments.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync($"/api/assignments/{id}/withdraw", new { expectedRevision = 1 })).StatusCode);
        using var replay = await parent.PostAsJsonAsync("/api/assignments", new { childId = profile["id"]!.GetValue<Guid>(), snapshotId = snapshot });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(status, (await replay.Content.ReadFromJsonAsync<JsonNode>())!["status"]!.GetValue<string>());
        Assert.Empty((await child.GetFromJsonAsync<JsonNode>("/api/child/assignments?state=available"))!["items"]!.AsArray());
        Assert.Single((await child.GetFromJsonAsync<JsonNode>("/api/child/assignments?state=submitted"))!["items"]!.AsArray());
        Assert.Single((await parent.GetFromJsonAsync<JsonNode>($"/api/assignments?status={status}&childId={profile["id"]!.GetValue<Guid>()}"))!["items"]!.AsArray());
        Assert.Empty((await parent.GetFromJsonAsync<JsonNode>($"/api/assignments?childId={Guid.NewGuid()}"))!["items"]!.AsArray());
    }

    [Fact]
    public async Task Assignments_are_owned_and_duplicate_creation_keeps_the_same_withdrawn_history()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        var profile = await Create(parent);
        var sibling = await Create(parent, "אח");
        var foreign = await Create(stranger);
        using var child = await h.Activate(parent, profile);
        using var otherChild = await h.Activate(parent, sibling);
        using var foreignChild = await h.Activate(stranger, foreign);
        var snapshotId = await Snapshot(parent);
        var body = new { childId = profile["id"]!.GetValue<Guid>(), snapshotId };
        var responses = await Task.WhenAll(parent.PostAsJsonAsync("/api/assignments", body), parent.PostAsJsonAsync("/api/assignments", body));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var assignment = (await responses[0].Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True(JsonNode.DeepEquals(assignment, await responses[1].Content.ReadFromJsonAsync<JsonNode>()));
        var path = "/api/assignments/" + assignment["id"]!.GetValue<Guid>();
        var childPath = path.Replace("/api/", "/api/child/", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path + "/withdraw", new { expectedRevision = 1 })).StatusCode);
        foreach (var client in new[] { otherChild, foreignChild })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(childPath)).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<JsonNode>("/api/child/assignments"))!["items"]!.AsArray());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await parent.GetAsync(childPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(path + "/withdraw", new { expectedRevision = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(path + "/withdraw", new { expectedRevision = 1 })).StatusCode);
        using var replay = await parent.PostAsJsonAsync("/api/assignments", body);
        var withdrawn = (await replay.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("withdrawn", withdrawn["status"]!.GetValue<string>());
        Assert.Equal(2, withdrawn["revision"]!.GetValue<long>());
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(path + "/withdraw", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await child.GetAsync(childPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherChild.GetAsync(childPath)).StatusCode);
        Assert.Empty((await child.GetFromJsonAsync<JsonNode>("/api/child/assignments"))!["items"]!.AsArray());
        // Withdrawn work is kept as history that only its own filter lists.
        Assert.Empty((await parent.GetFromJsonAsync<JsonNode>("/api/assignments"))!["items"]!.AsArray());
        Assert.Single((await parent.GetFromJsonAsync<JsonNode>("/api/assignments?status=withdrawn"))!["items"]!.AsArray());
    }

    [Fact]
    public async Task Withdrawn_work_returns_only_through_an_explicit_current_restore_for_an_enabled_child()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var assignment = await Assign(parent, profile, await Snapshot(parent));
        var path = "/api/assignments/" + assignment["id"]!.GetValue<Guid>();
        var childPath = path.Replace("/api/", "/api/child/", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(path + "/withdraw", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path + "/restore", new { expectedRevision = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(path + "/restore", new { expectedRevision = 1 })).StatusCode);
        using (var restored = await parent.PostAsJsonAsync(path + "/restore", new { expectedRevision = 2 }))
        {
            var summary = (await restored.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.Equal("assigned", summary["status"]!.GetValue<string>());
            Assert.Equal(3, summary["revision"]!.GetValue<long>());
        }
        // A repeated restore is idempotent, like a repeated withdrawal.
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(path + "/restore", new { expectedRevision = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await child.GetAsync(childPath)).StatusCode);
        Assert.Single((await parent.GetFromJsonAsync<JsonNode>("/api/assignments"))!["items"]!.AsArray());
        Assert.Empty((await parent.GetFromJsonAsync<JsonNode>("/api/assignments?status=withdrawn"))!["items"]!.AsArray());
        // Restoring reopens access, so it needs the same eligibility as a new assignment.
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(path + "/withdraw", new { expectedRevision = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "disabled", enabled = false, expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(path + "/restore", new { expectedRevision = 4 })).StatusCode);
    }

    [Fact]
    public async Task New_assignments_require_owned_reviewed_content_and_an_enabled_child()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var other = await h.App.ParentAsync();
        var profile = await Create(parent);
        var foreign = await Create(other);
        var snapshot = await Snapshot(parent);
        var foreignSnapshot = await Snapshot(other);
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        foreach (var id in new[] { Guid.NewGuid(), draft["id"]!.GetValue<Guid>(), foreignSnapshot })
            Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync("/api/assignments", new { childId = profile["id"]!.GetValue<Guid>(), snapshotId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync("/api/assignments", new { childId = foreign["id"]!.GetValue<Guid>(), snapshotId = snapshot })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "disabled", enabled = false, expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync("/api/assignments", new { childId = profile["id"]!.GetValue<Guid>(), snapshotId = snapshot })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/assignments", new { snapshotId = snapshot })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/assignments", new { childId = profile["id"]!.GetValue<Guid>(), snapshotId = snapshot, status = "completed" })).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100", "status=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await parent.GetAsync("/api/assignments?" + query)).StatusCode);
    }

    [Fact]
    public async Task Learner_content_has_exact_nested_allowlists_and_preserves_source_text_as_data()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        const string source = "<script>alert('answer')</script>\n\nKeep exact text: answer keys are data here.";
        var plan = Reading() with { Settings = Numeric(1).Settings };
        var draft = await ActivityDraftTests.Create(parent, plan);
        var edit = ActivityDraftTests.Edit(draft);
        edit["document"] = ActivityDraftTests.Document("private-parent-key");
        edit["document"]!["questions"]![0]!["interaction"]!["type"] = "text-input";
        edit["document"]!["materials"] = new JsonArray(new JsonObject { ["id"] = MaterialId, ["title"] = "Source", ["body"] = source });
        draft = await ActivityDraftTests.Seed(parent, draft, edit);
        using var release = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
        var assignment = await Assign(parent, profile, snapshot);
        var result = (await child.GetFromJsonAsync<JsonNode>("/api/child/assignments/" + assignment["id"]!.GetValue<Guid>()))!;
        Properties(result, "id", "status", "revision", "createdAtUtc", "document");
        var document = result["document"]!;
        Properties(document, "title", "instructions", "materials", "questions");
        var material = Assert.Single(document["materials"]!.AsArray())!;
        Properties(material, "id", "title", "body");
        Assert.Equal(source, material["body"]!.GetValue<string>());
        var question = Assert.Single(document["questions"]!.AsArray())!;
        Properties(question, "id", "prompt", "interaction", "points");
        Properties(question["interaction"]!, "type", "options");
        var inbox = (await child.GetFromJsonAsync<JsonNode>("/api/child/assignments"))!;
        Properties(inbox, "items", "page", "pageSize", "hasMore");
        Properties(Assert.Single(inbox["items"]!.AsArray())!, "id", "title", "status", "revision", "createdAtUtc", "hasStarted");
        Assert.False(inbox["items"]![0]!["hasStarted"]!.GetValue<bool>());
        var parentDocument = (await parent.GetFromJsonAsync<JsonNode>("/api/instances/" + snapshot))!["document"]!;
        Assert.True(JsonNode.DeepEquals(parentDocument["questions"]![0]!["prompt"], question["prompt"]));
        Assert.NotNull(parentDocument["questions"]![0]!["answer"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await child.GetAsync("/api/child/assignments?state=invalid")).StatusCode);
        Assert.Empty((await child.GetFromJsonAsync<JsonNode>("/api/child/assignments?state=submitted"))!["items"]!.AsArray());
    }

    internal static void Properties(JsonNode node, params string[] names) => Assert.Equal(names.Order(), node.AsObject().Select(p => p.Key).Order());
}
