using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityReleaseTests
{
    [Theory]
    [InlineData("target", HttpStatusCode.Created)]
    [InlineData("range", HttpStatusCode.BadRequest)]
    public async Task Only_strict_length_expectations_block_reviewed_release(string mode, HttpStatusCode expected)
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var length = mode == "range" ? new LengthExpectation(mode, Lower: 100, Upper: 150) : new(mode, 100);
        var plan = Reading() with { Settings = Numeric(1).Settings, Materials = [Reading().Materials[0] with { Length = length }] };
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"] = Document();
        edit["document"]!["questions"]![0]!["interaction"]!["type"] = "text-input";
        edit["document"]!["materials"] = new JsonArray(new JsonObject { ["id"] = MaterialId, ["title"] = "כותרת שאינה נספרת", ["body"] = "שלום עולם" });
        draft = await Seed(parent, draft, edit);
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() });
        Assert.Equal(expected, release.StatusCode);
        if (expected == HttpStatusCode.Created)
        {
            var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.Equal(2, snapshot["measurements"]![0]!["actual"]!.GetValue<int>());
            Assert.Null(snapshot["measurements"]![0]!["satisfied"]);
            Assert.True(JsonNode.DeepEquals(draft["plan"], snapshot["plan"]));
            Assert.Null(snapshot["input"]);
            Assert.True(JsonNode.DeepEquals(draft["document"], snapshot["document"]));
            Assert.NotNull(snapshot["plan"]!["settings"]);
            Assert.NotNull(snapshot["reviewedByParentId"]);
            Assert.EndsWith("Z", snapshot["reviewedAtUtc"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Concurrent_save_and_release_have_one_winner_without_mixed_snapshot_revisions()
    {
        var barrier = new ConcurrentChanges();
        await using var app = new ApiFactory(services => services.AddScoped(provider => new LearningDbContext(
            new DbContextOptionsBuilder<LearningDbContext>(provider.GetRequiredService<DbContextOptions<LearningDbContext>>()).AddInterceptors(barrier).Options)));
        using var parent = await app.ParentAsync();
        var draft = await ReadyDraft(parent);
        var edit = Edit(draft);
        edit["document"]!["title"] = "כותרת שנערכה";
        barrier.Enabled = true;
        var save = parent.PutAsJsonAsync(Path(draft), edit);
        var release = parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() });
        try { await barrier.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { barrier.Resume.TrySetResult(); }
        var responses = await Task.WhenAll(save, release);
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        if (responses[1].IsSuccessStatusCode)
        {
            var snapshot = await db.TaskSnapshots.SingleAsync();
            Assert.Equal("תרגול", snapshot.Title);
            Assert.Equal(2, snapshot.SourceDraftRevision);
        }
        else Assert.Empty(await db.TaskSnapshots.ToListAsync());
        Assert.Equal(responses[1].IsSuccessStatusCode ? 2 : 3, (await db.ActivityDrafts.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Concurrent_exact_release_requests_return_one_snapshot()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ReadyDraft(parent);
        var request = new { expectedRevision = draft["revision"]!.GetValue<long>() };
        var replies = await Task.WhenAll(parent.PostAsJsonAsync(Path(draft) + "/release", request), parent.PostAsJsonAsync(Path(draft) + "/release", request));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK);
        var first = (await replies[0].Content.ReadFromJsonAsync<JsonNode>())!;
        var second = (await replies[1].Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(first["id"]!.GetValue<Guid>(), second["id"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Snapshot_copy_clears_review_and_remains_independent_after_source_draft_and_snapshot_deletion()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await ReadyDraft(parent);
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 2 });
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        var snapshotId = snapshot["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), snapshotId })).StatusCode);
        using var copy = await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), snapshotId });
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        var cloned = (await copy.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Null(cloned["releasedSnapshotId"]);
        Assert.Null(cloned["activeOperationId"]);
        Assert.Equal(1, cloned["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(snapshot["document"], cloned["document"]));
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(Path(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync(Path(cloned))).StatusCode);
        var edit = Edit(cloned);
        edit["document"]!["title"] = "עותק ערוך";
        await Save(parent, cloned, edit);
    }

    internal static async Task<JsonNode> ReadyDraft(HttpClient parent)
    {
        var draft = await Create(parent, Numeric(1));
        var edit = Edit(draft);
        edit["document"] = Document();
        return await Seed(parent, draft, edit);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("format")]
    [InlineData("answer")]
    public async Task Release_requires_complete_question_count_format_and_answer(string fault)
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ReadyDraft(parent);
        var edit = Edit(draft);
        if (fault == "count") edit["document"]!["questions"] = new JsonArray();
        if (fault == "format") edit["document"]!["questions"]![0]!["interaction"]!["type"] = "text-input";
        if (fault == "answer") edit["document"]!["questions"]![0]!["answer"]!["value"] = "not a number";
        draft = await Seed(parent, draft, edit);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 3 })).StatusCode);
        Assert.Equal(0, (await parent.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/instances")).GetArrayLength());
    }

    [Fact]
    public async Task Snapshot_insert_failure_rolls_back_release_and_keeps_the_draft_editable()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ReadyDraft(parent);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectSnapshotInsert BEFORE INSERT ON TaskSnapshots BEGIN SELECT RAISE(ABORT, 'private snapshot error'); END;");
        }
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.InternalServerError, release.StatusCode);
        Assert.DoesNotContain("private snapshot error", await release.Content.ReadAsStringAsync());
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(2, saved["revision"]!.GetValue<long>());
        Assert.Null(saved["releasedSnapshotId"]);
        await Save(parent, saved, Edit(saved));
    }

    private sealed class ConcurrentChanges : SaveChangesInterceptor
    {
        private int entered;
        internal bool Enabled { get; set; }
        internal TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<ActivityDraft>().Any(e => e.State == EntityState.Modified))
            {
                if (Interlocked.Increment(ref entered) == 2) BothEntered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task Release_freezes_the_reviewed_revision_and_exact_replay_never_recreates_a_deleted_snapshot()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var edit = Edit(draft);
        edit["document"] = Document();
        draft = await Seed(parent, draft, edit);
        var request = new { expectedRevision = draft["revision"]!.GetValue<long>() };
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", request);
        Assert.True(release.StatusCode == HttpStatusCode.Created, await release.Content.ReadAsStringAsync());
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        var id = snapshot["id"]!.GetValue<Guid>();
        Assert.Equal(request.expectedRevision, snapshot["sourceDraftRevision"]!.GetValue<long>());
        Assert.Equal("2", snapshot["document"]!["questions"]![0]!["answer"]!["value"]!.GetValue<string>());
        using var replay = await parent.PostAsJsonAsync(Path(draft) + "/release", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(draft), Edit(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await parent.PostAsJsonAsync(Path(draft) + "/release", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(Path(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync(Path(draft) + "/release", request)).StatusCode);
    }
}
