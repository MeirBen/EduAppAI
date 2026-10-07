using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.Integration.GenerationHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class GenerationRaceTests
{
    [Fact]
    public async Task A_generation_start_fences_a_release_that_already_read_the_draft()
    {
        var barrier = new ReleaseBarrier();
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync(services => services.AddScoped(provider => new LearningDbContext(
            new DbContextOptionsBuilder<LearningDbContext>(provider.GetRequiredService<DbContextOptions<LearningDbContext>>()).AddInterceptors(barrier).Options)));
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        var release = parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 2 });
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await Start(parent, draft); }
        finally { barrier.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await release).StatusCode);
        using var scope = app.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Empty(await db.TaskSnapshots.ToListAsync());
        Assert.NotNull((await db.ActivityDrafts.SingleAsync()).ActiveOperationId);
    }

    private sealed class ReleaseBarrier : SaveChangesInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ActivityDraft>().Any(e => e.State == EntityState.Modified && e.Entity.ReleasedSnapshotId.HasValue))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Theory]
    [InlineData("edit", false)]
    [InlineData("edit", true)]
    [InlineData("undo", false)]
    [InlineData("undo", true)]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("reset", false)]
    [InlineData("reset", true)]
    public async Task Local_changes_fence_claims_and_late_provider_output(string action, bool duringCall)
    {
        await using var app = new GenerationHarness(Questions());
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Chat.BeforeResponse = async token => { entered.SetResult(token); await resume.Task; };
        app.Chat.Usage = new() { InputTokenCount = 10, OutputTokenCount = 20 };
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Supplied() with { Defaults = Numeric(1).Defaults });
        var operation = await Start(parent, draft);
        Task<bool>? work = null;
        CancellationToken providerToken = default;
        if (duringCall)
        {
            work = app.Worker.RunNextAsync(default);
            providerToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        try
        {
            if (action == "cancel")
            {
                using var cancelled = await parent.PostAsync(OperationPath(operation) + "/cancel", null);
                Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
                Assert.Equal("cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonNode>())!["status"]!.GetValue<string>());
                if (duringCall) Assert.True(providerToken.IsCancellationRequested);
                Assert.Equal(HttpStatusCode.OK, (await parent.PostAsync(OperationPath(operation) + "/cancel", null)).StatusCode);
            }
            else if (action == "delete") Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(Path(draft))).StatusCode);
            else if (action == "reset") Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync("/api/templates")).StatusCode);
            else
            {
                var edit = Edit(draft);
                if (action == "source") edit["plan"]!["materials"]![0]!["text"] = "מקור אחר — Hello!";
                else edit["document"]!["title"] = "עריכה מקומית";
                var saved = await Save(parent, draft, edit);
                if (action == "undo")
                {
                    edit = Edit(draft);
                    edit["expectedRevision"] = saved["revision"]!.DeepClone();
                    await Save(parent, saved, edit);
                }
            }
        }
        finally { resume.TrySetResult(); }
        if (work is not null) await work.WaitAsync(TimeSpan.FromSeconds(10));
        else await app.Worker.RunNextAsync(default);
        Assert.Equal(duringCall ? 1 : 0, app.Chat.Requests.Count);
        if (action is "delete" or "reset")
        {
            Assert.Equal(HttpStatusCode.NotFound, (await parent.GetAsync(OperationPath(operation))).StatusCode);
            using var scope = app.App.Services.CreateScope();
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<LearningDbContext>().GenerationOperations.ToListAsync());
            return;
        }
        var state = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal(action == "cancel" ? "cancelled" : "conflict", state["status"]!.GetValue<string>());
        var current = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Empty(current["document"]!["questions"]!.AsArray());
        Assert.Null(current["activeOperationId"]);
        if (duringCall && action == "cancel")
        {
            Assert.Equal(20, state["steps"]![0]!["usage"]!["outputTokens"]!.GetValue<long>());
            Assert.Null(state["artifacts"]!["steps"]![0]!["call"]);
            Assert.Equal(2, current["revision"]!.GetValue<long>());
        }
        else if (duringCall) Assert.NotNull(state["artifacts"]!["steps"]![0]!["candidate"]);
    }

    [Fact]
    public async Task Concurrent_starts_on_one_draft_admit_one_operation_and_same_key_replays_it()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateQuestions" };
        var replies = await Task.WhenAll(parent.PostAsJsonAsync(Path(draft) + "/operations", request), parent.PostAsJsonAsync(Path(draft) + "/operations", request));
        Assert.All(replies, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        using var scope = app.App.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<LearningDbContext>().GenerationOperations.ToListAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(Path(draft) + "/operations", request with { operationKey = Guid.NewGuid() })).StatusCode);
    }
}
