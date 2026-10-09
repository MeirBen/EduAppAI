using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.AssignmentTests;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;
using static FamilyLearning.Api.Tests.Integration.ChildSessionTests;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class AssignmentRetentionTests
{
    [Fact]
    public async Task Removing_assigned_content_archives_it_without_changing_frozen_content_or_existing_assignments()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        var profile = await Create(parent);
        var sibling = await Create(parent, "sibling");
        using var child = await h.Activate(parent, profile);
        var snapshotId = await Snapshot(parent);
        var assignment = await Assign(parent, profile, snapshotId);
        var original = (await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{snapshotId}"))!;
        var summary = Assert.Single((await parent.GetFromJsonAsync<JsonNode>("/api/instances"))!.AsArray())!;
        Assert.True(summary["hasAssignments"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{snapshotId}")).StatusCode);
        var archived = (await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{snapshotId}"))!;
        Assert.True(JsonNode.DeepEquals(original["document"], archived["document"]));
        Assert.NotNull(archived["archivedAtUtc"]);
        Assert.Empty((await parent.GetFromJsonAsync<JsonNode>("/api/instances"))!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await child.GetAsync("/api/child/assignments/" + assignment["id"]!.GetValue<Guid>())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync("/api/assignments/" + assignment["id"]!.GetValue<Guid>())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync("/api/assignments", new { childId = sibling["id"]!.GetValue<Guid>(), snapshotId })).StatusCode);
        await parent.PutAsJsonAsync(Path(profile), new { name = profile["name"]!.GetValue<string>(), enabled = false, expectedRevision = 1 });
        using var replay = await parent.PostAsJsonAsync("/api/assignments", new { childId = profile["id"]!.GetValue<Guid>(), snapshotId });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(assignment, await replay.Content.ReadFromJsonAsync<JsonNode>()));
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.True(JsonNode.DeepEquals(archived, await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{snapshotId}")));
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var stored = await db.TaskSnapshots.SingleAsync(s => s.Id == snapshotId);
            var history = await GenerationHistoryReader.ReadAsync(db, stored.FamilyId, Guid.NewGuid(), new TaskDocument("empty", null, [], []), default);
            Assert.Contains(original["document"]!["questions"]![0]!["prompt"]!.GetValue<string>(), history.Questions);
        }
        var unassigned = await Snapshot(parent);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{unassigned}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.GetAsync($"/api/instances/{unassigned}")).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Assignment_and_snapshot_removal_serialize_without_dangling_work(bool assignmentFirst)
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var child = await Create(parent);
        var snapshotId = await Snapshot(parent);
        var body = new { childId = child["id"]!.GetValue<Guid>(), snapshotId };
        pause.Arm();
        var waiting = assignmentFirst ? parent.DeleteAsync($"/api/instances/{snapshotId}") : parent.PostAsJsonAsync("/api/assignments", body);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var first = assignmentFirst ? await parent.PostAsJsonAsync("/api/assignments", body) : await parent.DeleteAsync($"/api/instances/{snapshotId}");
            Assert.Equal(assignmentFirst ? HttpStatusCode.Created : HttpStatusCode.NoContent, first.StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(assignmentFirst ? HttpStatusCode.NoContent : HttpStatusCode.NotFound, (await waiting).StatusCode);
        var assignments = (await parent.GetFromJsonAsync<JsonNode>("/api/assignments"))!["items"]!.AsArray();
        Assert.Equal(assignmentFirst ? 1 : 0, assignments.Count);
        Assert.Equal(assignmentFirst ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await parent.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
    }

    [Fact]
    public async Task A_duplicate_create_after_withdrawal_returns_the_withdrawn_assignment()
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var child = await Create(parent);
        var snapshotId = await Snapshot(parent);
        var assignment = await Assign(parent, child, snapshotId);
        pause.Arm();
        var replay = parent.PostAsJsonAsync("/api/assignments", new { childId = child["id"]!.GetValue<Guid>(), snapshotId });
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync("/api/assignments/" + assignment["id"]!.GetValue<Guid>() + "/withdraw", new { expectedRevision = 1 })).StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal("withdrawn", (await (await replay).Content.ReadFromJsonAsync<JsonNode>())!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pagination_reaches_old_work_and_reset_is_atomic_unbounded_and_family_scoped()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var stranger = await h.App.ParentAsync();
        var profile = await Create(parent);
        var foreign = await Create(stranger);
        using var child = await h.Activate(parent, profile);
        var foreignAssignment = await Assign(stranger, foreign, await Snapshot(stranger));
        using var foreignClient = await h.Activate(stranger, foreign);
        var foreignSession = await Start(foreignClient, SessionPath(foreignAssignment));
        var snapshotId = await Snapshot(parent);
        var ids = new List<Guid> { snapshotId };
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var source = await db.TaskSnapshots.SingleAsync(s => s.Id == snapshotId);
            for (var i = 0; i < 100; i++)
            {
                var copy = new TaskSnapshot(source.FamilyId, Guid.NewGuid(), source.SourceDraftRevision, source.Title,
                    source.PlanJson, source.DocumentJson, source.MeasurementsJson,
                    source.EngineRevision, null, source.CreatedByParentId, source.DraftCreatedAtUtc, source.ReviewedByParentId, source.ReviewedAtUtc);
                db.Add(copy);
                ids.Add(copy.Id);
            }
            await db.SaveChangesAsync();
        }
        foreach (var id in ids) await Start(child, SessionPath(await Assign(parent, profile, id)));
        foreach (var client in new[] { parent, child })
        {
            var path = client == parent ? "/api/assignments" : "/api/child/assignments";
            var first = (await client.GetFromJsonAsync<JsonNode>(path + "?pageSize=100"))!;
            var last = (await client.GetFromJsonAsync<JsonNode>(path + "?pageSize=100&page=2"))!;
            Assert.Equal(100, first["items"]!.AsArray().Count);
            Assert.True(first["hasMore"]!.GetValue<bool>());
            Assert.Single(last["items"]!.AsArray());
            Assert.False(last["hasMore"]!.GetValue<bool>());
            Assert.Equal(101, first["items"]!.AsArray().Concat(last["items"]!.AsArray()).Select(x => x!["id"]!.GetValue<Guid>()).Distinct().Count());
        }
        using (var scope = h.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LearningDbContext>().Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER prevent_snapshot_reset BEFORE DELETE ON TaskSnapshots BEGIN SELECT RAISE(ABORT, 'isolated'); END;");
        Assert.Equal(HttpStatusCode.InternalServerError, (await parent.DeleteAsync("/api/learning-data")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await child.GetAsync("/api/child/auth/me")).StatusCode);
        Assert.Single((await parent.GetFromJsonAsync<JsonNode>("/api/assignments?pageSize=100&page=2"))!["items"]!.AsArray());
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            Assert.Equal(102, await db.TaskSessions.CountAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER prevent_snapshot_reset;");
        }
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync("/api/learning-data")).StatusCode);
        Assert.Empty((await parent.GetFromJsonAsync<JsonNode>("/api/assignments"))!["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/assignments/" + foreignAssignment["id"]!.GetValue<Guid>())).StatusCode);
        using var verification = h.App.Services.CreateScope();
        var remaining = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Single(await remaining.TaskSnapshots.ToArrayAsync());
        Assert.Single(await remaining.Children.ToArrayAsync());
        Assert.Single(await remaining.TaskSessions.ToArrayAsync());
        Assert.True(JsonNode.DeepEquals(foreignSession, await foreignClient.GetFromJsonAsync<JsonNode>(SessionPath(foreignAssignment))));
    }
}
