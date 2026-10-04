using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class GenerationOperationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_revision_owns_only_unchanged_content(bool externalEdit)
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var operation = await GenerationHarness.Start(parent, draft);
        if (externalEdit)
        {
            var edit = Edit(draft);
            edit["document"]!["title"] = "ממכשיר אחר";
            await Save(parent, draft, edit);
        }
        using var response = await parent.PostAsync(GenerationHarness.OperationPath(operation) + "/cancel", null);
        var cancelled = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(externalEdit ? 1 : 2, cancelled["expectedRevision"]!.GetValue<long>());
        var current = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(externalEdit ? 3 : 2, current["revision"]!.GetValue<long>());
        Assert.Equal(externalEdit ? "ממכשיר אחר" : draft["document"]!["title"]!.GetValue<string>(), current["document"]!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Oversized_provider_identifiers_remain_unknown_without_losing_known_usage_or_valid_content()
    {
        await using var app = new GenerationHarness(GenerationHarness.Questions());
        app.Chat.ModelId = new string('א', 20_000);
        app.Chat.ResponseId = new string('ב', 20_000);
        app.Chat.Usage = new() { OutputTokenCount = 42 };
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var operation = await GenerationHarness.Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        var state = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal("completed", state["status"]!.GetValue<string>());
        Assert.Null(state["steps"]![0]!["usage"]!["model"]);
        Assert.Null(state["steps"]![0]!["usage"]!["responseId"]);
        Assert.Equal(42, state["steps"]![0]!["usage"]!["outputTokens"]!.GetValue<long>());
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal("unknown", saved["document"]!["questions"]![0]!["origin"]!["generation"]!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Material_candidate_that_overflows_the_retained_document_keeps_the_saved_draft_readable()
    {
        var response = System.Text.Json.JsonSerializer.Serialize(new { materials = new[] { new { id = MaterialId, title = (string?)null, body = new string('א', 4000) } } });
        await using var app = new GenerationHarness(response);
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(10) with { Materials = [Reading().Materials[0] with { Length = null }] });
        var edit = Edit(draft);
        edit["document"] = Document();
        var question = edit["document"]!["questions"]![0]!.DeepClone();
        question["prompt"] = new string('ב', 500);
        edit["document"]!["questions"] = new JsonArray(Enumerable.Range(0, 10).Select(_ => question.DeepClone()).ToArray());
        draft = await Save(parent, draft, edit);
        var operation = await GenerationHarness.Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        var state = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal("failed", state["status"]!.GetValue<string>());
        Assert.NotNull(state["artifacts"]!["steps"]![0]!["diagnostics"]!["document"]);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.True(JsonNode.DeepEquals(draft["document"], saved["document"]));
        Assert.Equal(2, saved["revision"]!.GetValue<long>());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Single(app.Chat.Requests);
    }

    [Fact]
    public async Task Hosted_worker_stops_the_host_on_an_unexpected_defect_instead_of_swallowing_it()
    {
        await using var app = new GenerationHarness();
        app.Chat.BeforeResponse = _ => throw new InvalidOperationException("unexpected worker defect");
        using var parent = await app.ParentAsync(services => services.AddHostedService(p => p.GetRequiredService<GenerationWorker>()));
        var worker = app.Worker;
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = app.App.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());
        await GenerationHarness.Start(parent, await Create(parent, Numeric(1)));
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);
    }

    [Fact]
    public async Task Evidence_storage_rejects_a_third_step_and_more_than_two_MiB_without_replacing_the_checkpoint()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        await Create(parent, Numeric(1));
        using var scope = app.App.Services.CreateScope();
        var draft = await scope.ServiceProvider.GetRequiredService<LearningDbContext>().ActivityDrafts.SingleAsync();
        var operation = new GenerationOperation(draft, new(Guid.NewGuid(), 1, "GenerateActivity"), Resolve(Numeric(1)),
            StoredJson.Read<Api.TaskEngine.Models.TaskDocument>(draft.DocumentJson), "questions", app.Worker.ProfileFingerprint, app.Clock.Now.UtcDateTime);
        var original = operation.ArtifactsJson;
        var artifacts = operation.Artifacts;
        Assert.False(operation.StoreArtifacts(artifacts, [new("questions", "calling"), new("questions", "calling"), new("questions", "calling")]));
        var oversized = new GenerationStepArtifact("questions", artifacts.Current, new AiCallEvidence { Request = new string('x', 2 * 1024 * 1024) });
        Assert.False(operation.StoreArtifacts(artifacts with { Steps = [oversized] }, [new("questions", "calling")]));
        Assert.Equal(original, operation.ArtifactsJson);
        Assert.Empty(operation.Steps);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("provider")]
    public async Task Expected_call_failure_releases_only_its_operation_and_the_next_operation_runs(string fault)
    {
        await using var app = new GenerationHarness(GenerationHarness.Questions());
        using var parent = await app.ParentAsync();
        var firstDraft = await Create(parent, Numeric(1));
        var first = await GenerationHarness.Start(parent, firstDraft);
        app.Clock.Now = app.Clock.Now.AddSeconds(1);
        var secondDraft = await Create(parent, Numeric(1));
        var second = await GenerationHarness.Start(parent, secondDraft);
        app.Chat.BeforeResponse = fault == "timeout" ? ct => Task.Delay(Timeout.Infinite, ct) : _ => throw new HttpRequestException("private provider data");
        await app.Worker.RunNextAsync(default);
        var failed = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(first)))!;
        Assert.Equal("failed", failed["status"]!.GetValue<string>());
        Assert.Equal(fault, failed["failure"]!.GetValue<string>());
        Assert.DoesNotContain("private provider data", failed.ToJsonString());
        Assert.Null((await parent.GetFromJsonAsync<JsonNode>(Path(firstDraft)))!["activeOperationId"]);
        app.Chat.BeforeResponse = null;
        await app.Worker.RunNextAsync(default);
        Assert.Equal("completed", (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(second)))!["status"]!.GetValue<string>());
        Assert.Equal(2, app.Chat.Requests.Count);
    }

    [Theory]
    [InlineData("ReplaceMaterial")]
    [InlineData("ReplaceQuestion")]
    public async Task Scoped_replacement_makes_one_call_and_preserves_unrelated_content(string kind)
    {
        var replacement = kind == "ReplaceMaterial" ? """{"id":"11111111111111111111111111111111","title":null,"body":"סיפור חדש"}""" :
            """{"prompt":"שאלה חדשה","interaction":{"type":"text-input","options":null},"answer":{"value":"חדש"},"points":1}""";
        await using var app = new GenerationHarness(GenerationHarness.Materials, GenerationHarness.Questions("text-input"), replacement);
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        await GenerationHarness.Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        await app.Worker.RunNextAsync(default);
        draft = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        var questionId = draft["document"]!["questions"]![0]!["id"]!.GetValue<string>();
        var operation = await GenerationHarness.Start(parent, draft, kind, kind == "ReplaceMaterial" ? MaterialId : questionId);
        await app.Worker.RunNextAsync(default);
        Assert.False(await app.Worker.RunNextAsync(default));
        var changed = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(questionId, changed["document"]!["questions"]![0]!["id"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(draft["document"]!["title"], changed["document"]!["title"]));
        if (kind == "ReplaceMaterial")
        {
            Assert.True(JsonNode.DeepEquals(draft["document"]!["questions"], changed["document"]!["questions"]));
            Assert.Contains("questions[0].stale", changed["diagnostics"]!.AsObject().Select(p => p.Key));
        }
        else Assert.True(JsonNode.DeepEquals(draft["document"]!["materials"], changed["document"]!["materials"]));
        Assert.Equal(3, app.Chat.Requests.Count);
        Assert.Equal("completed", (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Strict_manual_material_blocks_question_generation_before_any_operation_or_call()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var plan = Reading() with { Defaults = Numeric(1).Defaults, Materials = [Reading().Materials[0] with { Length = new("range", Lower: 3, Upper: 4) }] };
        var draft = await Create(parent, plan);
        var edit = Edit(draft);
        edit["document"]!["materials"] = new JsonArray(new JsonObject { ["id"] = MaterialId, ["body"] = "קצר" });
        draft = await Save(parent, draft, edit);
        foreach (var kind in new[] { "GenerateActivity", "GenerateQuestions" })
            Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/operations",
                new { operationKey = Guid.NewGuid(), expectedRevision = 2, kind })).StatusCode);
        using var scope = app.App.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<LearningDbContext>().GenerationOperations.ToListAsync());
        Assert.Empty(app.Chat.Requests);
    }

    [Fact]
    public async Task Admission_enforces_four_per_family_and_thirty_two_globally()
    {
        await using var app = new GenerationHarness();
        for (var family = 0; family < 8; family++)
        {
            using var parent = await app.ParentAsync();
            for (var i = 0; i < 4; i++) await GenerationHarness.Start(parent, await Create(parent, Numeric(1)));
            var extra = await Create(parent, Numeric(1));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await parent.PostAsJsonAsync(Path(extra) + "/operations",
                new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateQuestions" })).StatusCode);
        }
        using var ninthFamily = await app.ParentAsync();
        var blocked = await Create(ninthFamily, Numeric(1));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ninthFamily.PostAsJsonAsync(Path(blocked) + "/operations",
            new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateActivity" })).StatusCode);
        Assert.Empty(app.Chat.Requests);
    }

    [Fact]
    public async Task Lifetime_limit_blocks_only_new_operations_and_preserves_replay_edit_release_and_explicit_clone()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        StartGenerationRequest? original = null;
        using (var scope = app.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var row = await db.ActivityDrafts.SingleAsync();
            for (var i = 0; i < 128; i++)
            {
                var request = new StartGenerationRequest(Guid.NewGuid(), 2, "GenerateQuestions");
                original ??= request;
                var operation = new GenerationOperation(row, request, Resolve(Numeric(1)),
                    StoredJson.Read<Api.TaskEngine.Models.TaskDocument>(row.DocumentJson), "questions", app.Worker.ProfileFingerprint, app.Clock.Now.UtcDateTime);
                operation.Finish("completed", null, app.Clock.Now.UtcDateTime);
                db.GenerationOperations.Add(operation);
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await parent.PostAsJsonAsync(Path(draft) + "/operations", original! with { OperationKey = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await parent.PostAsJsonAsync(Path(draft) + "/operations", original)).StatusCode);
        draft = await Save(parent, draft, Edit(draft));
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 3 });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(HttpStatusCode.Created, (await parent.PostAsJsonAsync("/api/activity-drafts", new { snapshotId = snapshot["id"]!.GetValue<Guid>() })).StatusCode);
        Assert.Empty(app.Chat.Requests);
    }

    [Fact]
    public async Task Ten_family_starts_are_shared_with_authoring_and_replay_does_not_consume_a_permit()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        using (var scope = app.App.Services.CreateScope())
        {
            var familyId = (await scope.ServiceProvider.GetRequiredService<LearningDbContext>().ActivityDrafts.SingleAsync()).FamilyId;
            var limiter = app.App.Services.GetRequiredService<AiStartLimiter>();
            for (var i = 0; i < 9; i++) Assert.True(limiter.TryAcquire(familyId));
        }
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateActivity" };
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var operation = (await start.Content.ReadFromJsonAsync<JsonNode>())!;
        await parent.PostAsync(GenerationHarness.OperationPath(operation) + "/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, (await parent.PostAsJsonAsync(Path(draft) + "/operations", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await parent.PostAsJsonAsync(Path(draft) + "/operations", request with { operationKey = Guid.NewGuid(), expectedRevision = 2 })).StatusCode);
    }

    [Fact]
    public async Task Material_checkpoint_queues_questions_and_replay_survives_progress_and_completion()
    {
        await using var app = new GenerationHarness(GenerationHarness.Materials, GenerationHarness.Questions("text-input"));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateActivity" };
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var operation = (await start.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True(await app.Worker.RunNextAsync(default));
        var checkpoint = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Single(checkpoint["document"]!["materials"]!.AsArray());
        Assert.Empty(checkpoint["document"]!["questions"]!.AsArray());
        Assert.Equal(2, checkpoint["revision"]!.GetValue<long>());
        Assert.Single(app.Chat.Requests);
        using var replay = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(operation["id"]!.GetValue<Guid>(), (await replay.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>());
        Assert.True(await app.Worker.RunNextAsync(default));
        var completed = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal("completed", completed["status"]!.GetValue<string>());
        Assert.Equal(2, completed["steps"]!.AsArray().Count);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(3, saved["revision"]!.GetValue<long>());
        Assert.Equal(3, completed["expectedRevision"]!.GetValue<long>());
        Assert.Null(saved["activeOperationId"]);
        Assert.Empty(saved["diagnostics"]!.AsObject());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Equal(HttpStatusCode.Accepted, (await parent.PostAsJsonAsync(Path(draft) + "/operations", request)).StatusCode);
        Assert.Equal(2, app.Chat.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejected_stage_preserves_accepted_content_and_does_not_retry(bool rejectMaterial)
    {
        await using var app = new GenerationHarness(GenerationHarness.Materials, "{}", GenerationHarness.Questions("text-input"));
        using var parent = await app.ParentAsync();
        var plan = Reading() with { Defaults = Numeric(1).Defaults };
        if (rejectMaterial) plan = plan with { Materials = [plan.Materials[0] with { Length = new("range", Lower: 3, Upper: 4) }] };
        var draft = await Create(parent, plan);
        var operation = await GenerationHarness.Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        if (!rejectMaterial) await app.Worker.RunNextAsync(default);
        var failed = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal("failed", failed["status"]!.GetValue<string>());
        Assert.NotNull(failed["artifacts"]!["steps"]![0]!["call"]!["output"]);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(rejectMaterial ? 0 : 1, saved["document"]!["materials"]!.AsArray().Count);
        Assert.Null(saved["activeOperationId"]);
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Equal(rejectMaterial ? 1 : 2, app.Chat.Requests.Count);
        if (!rejectMaterial)
        {
            await GenerationHarness.Start(parent, saved, "GenerateQuestions");
            await app.Worker.RunNextAsync(default);
            Assert.Single((await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!["document"]!["questions"]!.AsArray());
            Assert.Equal(3, app.Chat.Requests.Count);
        }
    }

    [Fact]
    public async Task Checkpoint_failure_rolls_back_content_candidate_and_next_stage()
    {
        await using var app = new GenerationHarness(GenerationHarness.Materials);
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Defaults = Numeric(1).Defaults });
        var operation = await GenerationHarness.Start(parent, draft);
        using (var scope = app.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LearningDbContext>().Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER RejectApply BEFORE UPDATE ON ActivityDrafts WHEN NEW.Revision > OLD.Revision BEGIN SELECT RAISE(ABORT, 'checkpoint failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => app.Worker.RunNextAsync(default));
        var stored = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(1, stored["revision"]!.GetValue<long>());
        Assert.Empty(stored["document"]!["materials"]!.AsArray());
        var state = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal("calling", state["status"]!.GetValue<string>());
        Assert.Null(state["artifacts"]!["steps"]![0]!["call"]);
        Assert.Single(app.Chat.Requests);
    }

    [Fact]
    public async Task Start_is_owned_idempotent_and_replay_precedes_current_revision_checks()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateActivity" };
        var path = Path(draft) + "/operations";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path, request)).StatusCode);
        using var start = await parent.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var operation = (await start.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("queued", operation["status"]!.GetValue<string>());
        var edit = Edit(draft);
        edit["document"]!["title"] = "עריכה בזמן המתנה";
        await Save(parent, draft, edit);
        using var replay = await parent.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(operation["id"]!.GetValue<Guid>(), (await replay.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(path, request with { expectedRevision = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(start.Headers.Location)).StatusCode);
    }
}
