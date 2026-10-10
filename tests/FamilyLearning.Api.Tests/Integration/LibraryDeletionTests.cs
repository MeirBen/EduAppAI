using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class LibraryDeletionTests
{
    [Fact]
    public async Task Library_pages_list_every_item_once_newest_first_and_a_repeated_reset_clears_them_all()
    {
        await using var app = new ApiFactory();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(owner);
        var foreign = await ActivityReleaseTests.ReadyDraft(stranger);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var original = await db.ActivityDrafts.SingleAsync(d => d.Id == draft["id"]!.GetValue<Guid>());
            // Equal review times leave the snapshot order to the identifier tie-break; one save per row keeps the
            // storage order random, since a batch inserts in key order.
            var reviewed = DateTime.UtcNow;
            for (var index = 0; index < 30; index++)
            {
                db.ActivityDrafts.Add(new ActivityDraft(Guid.NewGuid(), original.FamilyId, original.Name, original.PlanJson,
                    original.DocumentJson, null, original.CreatedByParentId));
                db.TaskSnapshots.Add(new TaskSnapshot(original.FamilyId, Guid.NewGuid(), 1, original.Name, original.PlanJson,
                    original.DocumentJson, "[]", 1, null, original.CreatedByParentId, reviewed, original.CreatedByParentId, reviewed));
                await db.SaveChangesAsync();
            }
        }
        foreach (var (path, time, count) in new[] { ("/api/activity-drafts", "updatedAtUtc", 31), ("/api/instances", "createdAtUtc", 30) })
        {
            var listed = new List<JsonNode>();
            JsonNode page;
            var number = 0;
            do
            {
                page = (await owner.GetFromJsonAsync<JsonNode>($"{path}?page={++number}&pageSize=7"))!;
                listed.AddRange(page["items"]!.AsArray().Select(item => item!));
            } while (page["hasMore"]!.GetValue<bool>());
            Assert.Equal(count, listed.DistinctBy(item => item["id"]!.GetValue<Guid>()).Count());
            Assert.Equal(listed, listed.OrderByDescending(item => item[time]!.GetValue<DateTime>())
                .ThenByDescending(item => item["id"]!.GetValue<string>(), StringComparer.Ordinal));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/learning-data")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/learning-data")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync(ActivityDraftTests.Path(foreign))).StatusCode);
        using var verification = app.Services.CreateScope();
        var remaining = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Single(await remaining.ActivityDrafts.ToListAsync());
        Assert.Equal(2, await remaining.Users.CountAsync());
    }

    [Fact]
    public async Task Family_reset_deletes_drafts_and_snapshots_only_in_the_owned_family()
    {
        await using var app = new ApiFactory();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(owner);
        using var released = await owner.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        var snapshot = await released.Content.ReadFromJsonAsync<JsonElement>();
        var snapshotId = snapshot.GetProperty("id").GetGuid();
        var foreign = await ActivityReleaseTests.ReadyDraft(stranger);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(ActivityDraftTests.Path(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/learning-data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(ActivityDraftTests.Path(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync(ActivityDraftTests.Path(foreign))).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Families.CountAsync());
        Assert.Single(await db.ActivityDrafts.ToListAsync());
        Assert.Empty(await db.TaskSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Content_first_reset_failure_rolls_back_draft_and_snapshot_deletion()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        using var released = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectSnapshotDeletion BEFORE DELETE ON TaskSnapshots BEGIN SELECT RAISE(ABORT, 'private'); END;");
        }
        Assert.Equal(HttpStatusCode.InternalServerError, (await parent.DeleteAsync("/api/learning-data")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync(ActivityDraftTests.Path(draft))).StatusCode);
        Assert.Single((await parent.GetFromJsonAsync<JsonNode>("/api/instances"))!["items"]!.AsArray());
    }

    [Theory]
    [InlineData("/api/learning-data")]
    [InlineData("/api/instances/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async Task Deletion_requires_authentication_and_csrf(string path)
    {
        using var app = new ApiFactory();
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(path)).StatusCode);
        using var parent = await app.ParentAsync();
        parent.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.DeleteAsync(path)).StatusCode);
    }

}
