using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.Integration.GenerationHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class MaterialIdeaOperationTests
{
    [Fact]
    public async Task Generated_material_starts_with_ideas_without_changing_content()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading());
        var operation = await Start(parent, draft);
        Assert.Equal("material-ideas", operation["stage"]!.GetValue<string>());
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(draft["revision"]!.GetValue<long>(), saved["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(draft["document"], saved["document"]));
        Assert.Empty(app.Chat.Requests);
    }

    [Fact]
    public async Task Idea_checkpoint_resumes_writing_without_repeating_the_call_and_the_idea_stays_through_edits()
    {
        await using var app = new GenerationHarness(Ideas, Materials, UnchangedPolish, Questions("text-input"));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Settings = Numeric(1).Settings });
        var operation = await Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        var checkpoint = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("materials", checkpoint["stage"]!.GetValue<string>());
        Assert.Equal("queued", checkpoint["status"]!.GetValue<string>());
        Assert.Equal(1, checkpoint["expectedRevision"]!.GetValue<long>());
        var selected = checkpoint["artifacts"]!["selectedIdea"]!.DeepClone();
        Assert.Equal(1, (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!["revision"]!.GetValue<long>());
        await app.Worker.RecoverAsync(default);
        await app.Worker.RunNextAsync(default);
        using var writing = JsonDocument.Parse(app.Chat.Requests[1].Input.Split('\n')[^1]);
        Assert.Equal(selected["premise"]!.GetValue<string>(), writing.RootElement.GetProperty("idea").GetProperty("premise").GetString());
        await app.Worker.RunNextAsync(default);
        Assert.True(await app.Worker.RunNextAsync(default));
        Assert.False(await app.Worker.RunNextAsync(default));
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(2, saved["revision"]!.GetValue<long>());
        Assert.Equal(4, app.Chat.Requests.Count);
        Assert.True(JsonNode.DeepEquals(selected, saved["document"]!["materials"]![0]!["idea"]));
        var edit = Edit(saved);
        edit["document"]!["materials"]![0]!["body"] = "תוכן חדש";
        saved = await Save(parent, saved, edit);
        Assert.True(JsonNode.DeepEquals(selected, saved["document"]!["materials"]![0]!["idea"]));
    }

    [Fact]
    public async Task Rejected_ideas_do_not_write_or_change_the_draft()
    {
        await using var app = new GenerationHarness("""{"ideas":[]}""");
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading());
        var operation = await Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        var failed = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("failed", failed["status"]!.GetValue<string>());
        Assert.Equal("material-ideas", failed["stage"]!.GetValue<string>());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Single(app.Chat.Requests);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(1, saved["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(draft["document"], saved["document"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admission_freezes_owned_history_without_answers_and_each_stage_receives_only_its_part(bool paddedPrompt)
    {
        await using var app = new GenerationHarness(Ideas, Materials, UnchangedPolish, Questions("text-input"));
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var family = await FamilyId(parent);
        await AddDraftWithIdea(app, family, "owned", (paddedPrompt ? new string(' ', 300) : "") + "owned-question");
        await AddDraftWithIdea(app, await FamilyId(stranger), "foreign");
        var draft = await Create(parent, Reading() with { Settings = Numeric(1).Settings });
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "Create" };
        using var started = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        var frozen = (await started.Content.ReadFromJsonAsync<JsonNode>())!["artifacts"]!["history"]!.DeepClone();
        Assert.Equal("owned-premise", frozen["ideas"]![0]!["premise"]!.GetValue<string>());
        Assert.Equal("owned-question", frozen["questions"]![0]!.GetValue<string>());
        await AddDraftWithIdea(app, family, "after-admission");
        using var replay = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        Assert.True(JsonNode.DeepEquals(frozen, (await replay.Content.ReadFromJsonAsync<JsonNode>())!["artifacts"]!["history"]));
        while (await app.Worker.RunNextAsync(default)) { }
        Assert.Equal(4, app.Chat.Requests.Count);
        var ideas = SentHistory(app.Chat.Requests[0].Input);
        var questions = SentHistory(app.Chat.Requests[3].Input);
        Assert.Contains("owned-premise", ideas);
        Assert.DoesNotContain("owned-question", ideas);
        Assert.DoesNotContain("after-admission", ideas);
        Assert.Contains("owned-question", questions);
        Assert.DoesNotContain("owned-premise", questions);
        Assert.DoesNotContain("after-admission", questions);
        // Writing and polishing receive no history.
        Assert.All(app.Chat.Requests.Skip(1).Take(2), call => Assert.DoesNotContain("\"history\"", call.Input));
        Assert.All(new[] { ideas, questions }, history =>
        {
            Assert.DoesNotContain("secret-answer", history);
            Assert.DoesNotContain("foreign", history);
        });
        Assert.Equal("completed", (await parent.GetFromJsonAsync<JsonNode>(started.Headers.Location))!["status"]!.GetValue<string>());

        static string SentHistory(string input)
        {
            using var sent = JsonDocument.Parse(input.Split('\n')[^1]);
            return sent.RootElement.GetProperty("history").GetRawText();
        }
    }

    [Fact]
    public async Task History_is_bounded_and_deduplicated()
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var family = await FamilyId(parent);
        for (var i = 0; i < 14; i++) await AddDraftWithIdea(app, family, $"draft-{i}", $"draft-{i}-question" + new string('ב', 320));
        using var scope = app.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        // The current draft is also a stored row here, so its content is read twice and must appear once.
        var current = (await db.ActivityDrafts.OrderByDescending(d => d.UpdatedAtUtc).FirstAsync()).Document;
        var history = await GenerationHistoryReader.ReadAsync(db, family, Guid.Empty, current, default);
        Assert.Equal(8, history.Ideas.Length);
        Assert.Equal(12, history.Questions.Length);
        Assert.Equal(history.Ideas.Length, history.Ideas.Distinct().Count());
        Assert.Equal(history.Questions.Length, history.Questions.Distinct().Count());
        Assert.All(history.Questions, prompt => Assert.InRange(prompt.Length, 1, 300));
        Assert.DoesNotContain("secret-answer", StoredJson.Write(history));
    }

    [Fact]
    public async Task Released_ideas_enter_history_once_and_only_for_their_family()
    {
        await using var app = new GenerationHarness(Ideas, Materials, UnchangedPolish, Questions("text-input"));
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var prior = await app.GenerateAsync(parent, await Create(parent, Reading() with { Settings = Numeric(1).Settings }));
        var snapshot = await Release(parent, prior);
        await Release(stranger, await SaveQuestion(stranger, "foreign-question"));
        using var copy = await parent.PostAsJsonAsync("/api/activity-drafts", new { snapshotId = snapshot["id"]!.GetValue<Guid>() });
        Assert.True(copy.IsSuccessStatusCode, await copy.Content.ReadAsStringAsync());
        var history = (await Start(parent, await Create(parent, Reading())))["artifacts"]!["history"]!;
        Assert.True(JsonNode.DeepEquals(prior["document"]!["materials"]![0]!["idea"], Assert.Single(history["ideas"]!.AsArray())));
        Assert.Single(history["questions"]!.AsArray());
        Assert.DoesNotContain("foreign", history.ToJsonString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edit_or_cancel_after_the_idea_checkpoint_prevents_a_writer_call(bool cancel)
    {
        await using var app = new GenerationHarness(Ideas);
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading());
        var operation = await Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        if (cancel) await parent.PostAsync(OperationPath(operation) + "/cancel", null);
        else
        {
            var edit = Edit(draft);
            edit["document"]!["title"] = "עריכה אחרי בחירת רעיון";
            Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(draft), edit)).StatusCode);
            await parent.PostAsync(OperationPath(operation) + "/cancel", null);
        }
        await app.Worker.RunNextAsync(default);
        var result = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("cancelled", result["status"]!.GetValue<string>());
        Assert.Single(app.Chat.Requests);
        Assert.False(await app.Worker.RunNextAsync(default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edit_or_cancel_during_idea_generation_cannot_queue_a_writer(bool cancel)
    {
        await using var app = new GenerationHarness(Ideas);
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading());
        var operation = await Start(parent, draft);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Chat.BeforeResponse = async _ => { entered.SetResult(); await release.Task; };
        var work = app.Worker.RunNextAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancel) await parent.PostAsync(OperationPath(operation) + "/cancel", null);
        else
        {
            var edit = Edit(draft);
            edit["document"]!["title"] = "עריכה בזמן יצירת רעיונות";
            Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(draft), edit)).StatusCode);
            await parent.PostAsync(OperationPath(operation) + "/cancel", null);
        }
        release.SetResult();
        await work;
        var result = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation)))!;
        Assert.Equal("cancelled", result["status"]!.GetValue<string>());
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Single(app.Chat.Requests);
        Assert.Empty((await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!["document"]!["materials"]!.AsArray());
    }

    private static async Task<Guid> FamilyId(HttpClient parent) =>
        (await parent.GetFromJsonAsync<JsonNode>("/api/auth/me"))!["familyId"]!.GetValue<Guid>();

    private static async Task AddDraftWithIdea(GenerationHarness app, Guid family, string marker, string? prompt = null)
    {
        var document = new TaskDocument("", null,
            [new(MaterialId, 1, null, marker + "-body", new("generated"), new("accepted", []), new(marker + "-premise", marker + "-structure"))],
            [new("question", prompt ?? marker + "-question", new("text-input"), new(marker + "-secret-answer"), 1, new("manual"), new("accepted", []))]);
        using var scope = app.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        db.ActivityDrafts.Add(new(family, marker, "{}", StoredJson.Write(document), null, "test-parent"));
        await db.SaveChangesAsync();
    }

    private static async Task<JsonNode> SaveQuestion(HttpClient parent, string prompt)
    {
        var draft = await Create(parent, Numeric(1));
        var edit = Edit(draft);
        edit["document"] = Document();
        edit["document"]!["questions"]![0]!["prompt"] = prompt;
        return await Seed(parent, draft, edit);
    }

    private static async Task<JsonNode> Release(HttpClient parent, JsonNode draft)
    {
        using var response = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = draft["revision"]!.GetValue<long>() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }
}
