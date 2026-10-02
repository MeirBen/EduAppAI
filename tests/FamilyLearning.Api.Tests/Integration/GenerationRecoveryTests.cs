using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.Integration.GenerationHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class GenerationRecoveryTests
{
    [Fact]
    public async Task Evidence_overflow_keeps_the_known_failed_call_and_accepted_checkpoint_after_expiration()
    {
        await using var app = new GenerationHarness(Materials, new string('x', 32000));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        var operation = await Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        // Seed retained evidence near the storage ceiling so the next returned response cannot fit.
        using (var scope = app.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var stored = await db.GenerationOperations.SingleAsync();
            var artifacts = stored.Artifacts;
            artifacts.Steps[0].Call!.Request = new string('x', 2_070_000);
            Assert.True(stored.StoreArtifacts(artifacts, stored.Steps));
            await db.SaveChangesAsync();
        }
        app.Chat.Usage = new() { InputTokenCount = 15, OutputTokenCount = 42 };
        await app.Worker.RunNextAsync(default);
        var state = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("failed", state["status"]!.GetValue<string>());
        Assert.Equal("evidence-limit", state["failure"]!.GetValue<string>());
        Assert.Equal("failed", state["steps"]![1]!["outcome"]!.GetValue<string>());
        Assert.Equal(42, state["steps"]![1]!["usage"]!["outputTokens"]!.GetValue<long>());
        Assert.Null(state["steps"]![1]!["usage"]!["costCredits"]);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Single(saved["document"]!["materials"]!.AsArray());
        Assert.Empty(saved["document"]!["questions"]!.AsArray());
        Assert.Equal(2, saved["revision"]!.GetValue<long>());
        app.Clock.Now = app.Clock.Now.AddDays(7);
        await app.Worker.PurgeAsync(default);
        await app.Worker.RecoverAsync(default);
        state = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Null(state["artifacts"]);
        Assert.Equal(42, state["steps"]![1]!["usage"]!["outputTokens"]!.GetValue<long>());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Equal(2, app.Chat.Requests.Count);
    }

    [Theory]
    [InlineData("Ai:ApiKey", "rotated-test-secret", "completed")]
    [InlineData("Ai:Model", "another-isolated-model", "conflict")]
    [InlineData("Ai:ResponseFormat", "text", "conflict")]
    public async Task Restart_compares_the_nonsecret_profile_and_allows_credential_rotation(string setting, string value, string expected)
    {
        await using var app = new GenerationHarness(Questions());
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var operation = await Start(parent, draft);
        app.Configuration[setting] = value;
        using var restarted = new GenerationWorker(app.App.Services.GetRequiredService<IServiceScopeFactory>(),
            app.App.Services.GetRequiredService<AiGenerationService>(), app.Clock, Options.Create(new GenerationOperationOptions()),
            app.App.Services.GetRequiredService<IOptions<AiGenerationOptions>>(), app.Configuration);
        await restarted.RecoverAsync(default);
        await restarted.RunNextAsync(default);
        Assert.Equal(expected, (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!["status"]!.GetValue<string>());
        Assert.Equal(expected == "completed" ? 1 : 0, app.Chat.Requests.Count);
    }

    [Fact]
    public async Task Purge_is_bounded_to_thirty_two_tombstones_per_pass()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        await Create(parent, Numeric(1));
        using (var scope = app.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var draft = await db.ActivityDrafts.SingleAsync();
            for (var i = 0; i < 33; i++)
            {
                var operation = new GenerationOperation(draft, new(Guid.NewGuid(), 1, "GenerateQuestions"), Resolve(Numeric(1)),
                    FamilyLearning.Api.TaskEngine.TaskAssembly.CreateDocument(Resolve(Numeric(1))), "questions", app.Worker.ProfileFingerprint, app.Clock.Now.UtcDateTime);
                operation.Finish("failed", "provider", app.Clock.Now.UtcDateTime);
                db.GenerationOperations.Add(operation);
            }
            await db.SaveChangesAsync();
        }
        app.Clock.Now = app.Clock.Now.AddDays(7);
        await app.Worker.PurgeAsync(default);
        using var check = app.App.Services.CreateScope();
        var database = check.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(33, await database.GenerationOperations.CountAsync());
        Assert.Equal(1, await database.GenerationOperations.CountAsync(o => o.ArtifactsJson != null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_resumes_only_the_queued_stage_after_the_last_accepted_checkpoint(bool acceptedMaterial)
    {
        await using var app = new GenerationHarness(Materials, Questions("text-input"));
        using var parent = await app.ParentAsync(services => services.AddScoped(provider => new LearningDbContext(
            new DbContextOptionsBuilder<LearningDbContext>(provider.GetRequiredService<DbContextOptions<LearningDbContext>>())
                .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning)).Options)));
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        var operation = await Start(parent, draft);
        if (acceptedMaterial) await app.Worker.RunNextAsync(default);
        await app.Worker.RecoverAsync(default);
        for (var i = 0; i < 3; i++) await parent.GetAsync(OperationPath(operation));
        Assert.Equal(acceptedMaterial ? 1 : 0, app.Chat.Requests.Count);
        while (await app.Worker.RunNextAsync(default)) { }
        Assert.Equal(2, app.Chat.Requests.Count);
        Assert.Equal("completed", (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("EngineRevision")]
    [InlineData("SchemaVersion")]
    [InlineData("ProfileFingerprint")]
    public async Task Configuration_change_stops_a_queued_second_stage_and_preserves_material(string property)
    {
        await using var app = new GenerationHarness(Materials);
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        var operation = await Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        using (var scope = app.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var stored = await db.GenerationOperations.SingleAsync();
            db.Entry(stored).Property(property).CurrentValue = property == "ProfileFingerprint" ? "different" : -1;
            await db.SaveChangesAsync();
        }
        await app.Worker.RecoverAsync(default);
        Assert.False(await app.Worker.RunNextAsync(default));
        var state = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("conflict", state["status"]!.GetValue<string>());
        Assert.Equal("configuration-changed", state["failure"]!.GetValue<string>());
        Assert.Single((await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!["document"]!["materials"]!.AsArray());
        Assert.Single(app.Chat.Requests);
    }

    [Fact]
    public async Task Shutdown_leaves_calling_unknown_on_restart_without_retrying_or_erasing_accepted_material()
    {
        await using var app = new GenerationHarness(Materials, Questions("text-input"));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        var operation = await Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Chat.BeforeResponse = async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); };
        using var shutdown = new CancellationTokenSource();
        var work = app.Worker.RunNextAsync(shutdown.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        await app.Worker.RecoverAsync(default);
        var state = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("unknown", state["status"]!.GetValue<string>());
        Assert.Equal("unknown", state["steps"]![1]!["outcome"]!.GetValue<string>());
        Assert.Null(state["steps"]![1]!["usage"]);
        Assert.Equal("accepted", state["steps"]![0]!["outcome"]!.GetValue<string>());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Equal(2, app.Chat.Requests.Count);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Single(saved["document"]!["materials"]!.AsArray());
        Assert.Null(saved["activeOperationId"]);
    }

    [Fact]
    public async Task Seven_day_expiration_retains_replay_and_usage_but_never_starts_work()
    {
        await using var app = new GenerationHarness(Questions());
        app.Chat.Usage = new() { OutputTokenCount = 42 };
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateActivity" };
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        var operation = (await start.Content.ReadFromJsonAsync<JsonNode>())!;
        await app.Worker.RunNextAsync(default);
        app.Clock.Now = app.Clock.Now.AddDays(7).AddTicks(-1);
        await app.Worker.PurgeAsync(default);
        Assert.False((await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!["diagnosticsExpired"]!.GetValue<bool>());
        app.Clock.Now = app.Clock.Now.AddTicks(1);
        await app.Worker.PurgeAsync(default);
        using var replay = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        var expired = (await replay.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True(expired["diagnosticsExpired"]!.GetValue<bool>());
        Assert.Null(expired["artifacts"]);
        Assert.Equal(42, expired["steps"]![0]!["usage"]!["outputTokens"]!.GetValue<long>());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Single(app.Chat.Requests);
        await parent.DeleteAsync(Path(draft));
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync(Path(draft) + "/operations", request)).StatusCode);
    }
}
