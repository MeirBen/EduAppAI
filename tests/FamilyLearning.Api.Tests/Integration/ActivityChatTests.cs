using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.ActivityRevisionTests;
using static FamilyLearning.Api.Tests.TaskEngine.ContentGenerationTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityChatTests
{
    [Fact]
    public async Task Create_checkpoints_are_private_and_failure_applies_nothing()
    {
        await using var app = new GenerationHarness(GenerationHarness.Ideas, GenerationHarness.Materials, "{}");
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading() with { Settings = Numeric(1).Settings });
        var operation = await GenerationHarness.Start(parent, draft, "Create");
        for (var i = 0; i < 3; i++)
        {
            await app.Worker.RunNextAsync(default);
            var stored = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
            Assert.True(JsonNode.DeepEquals(draft["document"], stored["document"]));
            Assert.Equal(1, stored["revision"]!.GetValue<long>());
        }
        var state = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!;
        Assert.Equal("failed", state["status"]!.GetValue<string>());
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.False(saved["canUndo"]!.GetValue<bool>());
        Assert.Single(saved["chat"]!.AsArray());
    }

    [Fact]
    public async Task Changing_revision_is_atomic_replay_has_one_parent_turn_and_undo_restores_content()
    {
        var plan = Numeric(1);
        var changed = plan with { Name = "שם חדש" };
        await using var app = new GenerationHarness(GenerationHarness.Questions(), Serialize(new { result = new RevisionDecision(null, null, Change(changed)) }));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, plan);
        await GenerationHarness.Start(parent, draft, "Create");
        await app.Worker.RunNextAsync(default);
        draft = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(2, draft["revision"]!.GetValue<long>());
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 2, kind = "Revise", message = "שנה שם" };
        var path = Path(draft) + "/operations";
        using var started = await parent.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await parent.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(path, request with { message = "אחר" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(draft), Edit(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(Path(draft) + "/undo", new { expectedRevision = 2 })).StatusCode);
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal("שם חדש", saved["plan"]!["name"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(draft["document"], saved["document"]));
        var summary = saved["chat"]!.AsArray()[^1]!;
        Assert.Equal("", summary["text"]!.GetValue<string>());
        Assert.Contains("שם חדש", GenerationHarness.Reply(summary));
        Assert.Contains("שם הטיוטה", GenerationHarness.Reply(summary));
        Assert.True(saved["canUndo"]!.GetValue<bool>());
        Assert.Single(saved["chat"]!.AsArray(), t => t!["role"]!.GetValue<string>() == "parent");
        using var undo = await parent.PostAsJsonAsync(Path(draft) + "/undo", new { expectedRevision = 3 });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        saved = (await undo.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(4, saved["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(draft["plan"], saved["plan"]));
        Assert.False(saved["canUndo"]!.GetValue<bool>());
        Assert.Equal(2, app.Chat.Requests.Count);
    }

    [Fact]
    public async Task Chat_edits_the_title_and_instructions_in_one_step_that_undo_restores()
    {
        var plan = Numeric(1);
        var edit = Change(plan) with { Document = new("תרגול בלי ניקוד", "ענו במספר") };
        await using var app = new GenerationHarness(GenerationHarness.Questions(), Serialize(new { result = new RevisionDecision(null, null, edit) }));
        using var parent = await app.ParentAsync();
        var draft = await Created(app, parent, plan);
        Assert.Equal("תרגול", await LibraryName(parent));
        await GenerationHarness.Start(parent, draft, "Revise", "בלי ניקוד");
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal("תרגול בלי ניקוד", saved["document"]!["title"]!.GetValue<string>());
        Assert.Equal("תרגול בלי ניקוד", await LibraryName(parent));
        Assert.Equal("ענו במספר", saved["document"]!["instructions"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(draft["document"]!["questions"], saved["document"]!["questions"]));
        var reply = GenerationHarness.Reply(saved["chat"]!.AsArray()[^1]!);
        Assert.Contains("כותרת התוכן עודכנה", reply);
        Assert.Contains("ההוראות לילדים עודכנו", reply);
        Assert.Equal(2, app.Chat.Requests.Count);
        using var undo = await parent.PostAsJsonAsync(Path(draft) + "/undo", new { expectedRevision = 3 });
        Assert.True(JsonNode.DeepEquals(draft["document"], (await undo.Content.ReadFromJsonAsync<JsonNode>())!["document"]));
        Assert.Equal("תרגול", await LibraryName(parent));
    }

    [Fact]
    public async Task An_explicit_title_survives_the_question_rebuild_of_the_same_change()
    {
        var plan = Numeric(1);
        var rebuild = Change(plan with { Guidance = "שאלות מאתגרות" }) with { Document = new("שם שבחרתם", null) };
        await using var app = new GenerationHarness(GenerationHarness.Questions(),
            Serialize(new { result = new RevisionDecision(null, null, rebuild) }), GenerationHarness.Questions());
        using var parent = await app.ParentAsync();
        var draft = await Created(app, parent, plan);
        await GenerationHarness.Start(parent, draft, "Revise", "שאלות מאתגרות ושם חדש");
        await app.Worker.RunNextAsync(default);
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(3, app.Chat.Requests.Count);
        Assert.Equal("שם שבחרתם", saved["document"]!["title"]!.GetValue<string>());
        Assert.Equal("ענו", saved["document"]!["instructions"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Longer_instructions_fit_when_the_same_change_removes_questions_and_never_exceed_the_content_limit(bool removes)
    {
        // Sixteen long prompts bring the document near the content limit, which longer instructions alone would exceed.
        var plan = Numeric(16);
        var questions = Serialize(new QuestionCandidateBatch("תרגול", "ענו", Enumerable.Range(0, 16)
            .Select(i => new QuestionCandidate($"{new string('x', 490)} {i}?", new("numeric-input"), new("2"), 1)).ToArray()));
        await using var app = new GenerationHarness
        {
            Chat = new AiFixtures.ScriptedChat(questions)
            {
                Respond = input =>
                {
                    var first = JsonNode.Parse(input.Split('\n')[^1])!["document"]!["questions"]![0]!["id"]!.GetValue<string>();
                    var change = Change(removes ? plan with { Settings = plan.Settings with { QuestionCount = 1 } } : plan) with
                    { QuestionOrder = removes ? [first] : null, Document = new(null, new string('ה', 400)) };
                    return Serialize(new { result = new RevisionDecision(null, null, change) });
                }
            }
        };
        using var parent = await app.ParentAsync();
        var draft = await Created(app, parent, plan);
        var operation = await GenerationHarness.Start(parent, draft, "Revise", "הוראות מפורטות");
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        var status = (await parent.GetFromJsonAsync<JsonNode>(GenerationHarness.OperationPath(operation)))!["status"]!.GetValue<string>();
        Assert.Equal(removes ? "completed" : "failed", status);
        Assert.Equal(removes ? 400 : 3, saved["document"]!["instructions"]!.GetValue<string>().Length);
        Assert.Equal(removes ? 1 : 16, saved["document"]!["questions"]!.AsArray().Count);
    }

    /// <summary>A draft lists by its plan name until generated content gives it a learner title.</summary>
    private static async Task<JsonNode> Created(GenerationHarness app, HttpClient parent, LearningPlan plan)
    {
        var draft = await Create(parent, plan);
        Assert.Equal(plan.Name, await LibraryName(parent));
        await GenerationHarness.Start(parent, draft);
        await app.Worker.RunNextAsync(default);
        return (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
    }

    private static async Task<string> LibraryName(HttpClient parent) =>
        Assert.Single((await parent.GetFromJsonAsync<JsonNode[]>("/api/activity-drafts"))!)!["name"]!.GetValue<string>();

    [Fact]
    public async Task Material_label_rename_commits_without_content_calls_and_reports_the_saved_label()
    {
        var plan = Reading() with { Settings = Numeric(1).Settings };
        var changed = plan with { Materials = [plan.Materials[0] with { Label = "שם טקסט חדש" }] };
        await using var app = new GenerationHarness(GenerationHarness.Ideas, GenerationHarness.Materials,
            GenerationHarness.UnchangedPolish, GenerationHarness.Questions("text-input"),
            Serialize(new { result = new RevisionDecision(null, null, Change(changed)) }));
        using var parent = await app.ParentAsync();
        var draft = await app.GenerateAsync(parent, await Create(parent, plan));
        using var started = await parent.PostAsJsonAsync(Path(draft) + "/operations", new
        { operationKey = Guid.NewGuid(), expectedRevision = draft["revision"]!.GetValue<long>(), kind = "Revise", message = "שנה את תווית הטקסט" });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal("שם טקסט חדש", saved["plan"]!["materials"]![0]!["label"]!.GetValue<string>());
        Assert.Equal(draft["revision"]!.GetValue<long>() + 1, saved["revision"]!.GetValue<long>());
        Assert.Null(saved["activeOperation"]);
        Assert.Empty(saved["diagnostics"]!.AsObject());
        Assert.True(saved["canUndo"]!.GetValue<bool>());
        Assert.Equal(5, app.Chat.Requests.Count);
        var notice = GenerationHarness.Reply(saved["chat"]!.AsArray()[^1]!);
        Assert.Contains("שם טקסט חדש", notice);
        Assert.DoesNotContain("שוכתב", notice);
        Assert.DoesNotContain("שאלות", notice);
        var beforeDocument = draft["document"]!.DeepClone();
        beforeDocument["materials"]![0]!["acceptance"] = saved["document"]!["materials"]![0]!["acceptance"]!.DeepClone();
        beforeDocument["questions"]![0]!["acceptance"] = saved["document"]!["questions"]![0]!["acceptance"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(beforeDocument, saved["document"]));
    }

    [Fact]
    public async Task Refusal_and_cancel_preserve_revision_and_append_one_terminal_notice()
    {
        const string reply = "הסברים למפתח התשובות אינם נתמכים.";
        await using var app = new GenerationHarness(Serialize(new { result = new RevisionDecision(reply, null, null) }));
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric() with { Guidance = "הסברים למפתח התשובות" });
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "Revise", message = "הוסף הסברים למפתח התשובות" });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(1, saved["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(draft["plan"], saved["plan"]));
        Assert.Equal(reply, saved["chat"]![1]!["text"]!.GetValue<string>());
        var operation = await GenerationHarness.Start(parent, saved, "Create");
        for (var i = 0; i < 2; i++) await parent.PostAsync(GenerationHarness.OperationPath(operation) + "/cancel", null);
        saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal(1, saved["revision"]!.GetValue<long>());
        Assert.Equal(3, saved["chat"]!.AsArray().Count);
        Assert.Single(app.Chat.Requests);
    }

    [Fact]
    public async Task Noops_refusals_and_failures_keep_undo_while_manual_save_clears_it()
    {
        var plan = Numeric(1);
        await using var app = new GenerationHarness(GenerationHarness.Questions(),
            Serialize(new { result = new RevisionDecision(null, null, Change(plan)) }),
            Serialize(new { result = new RevisionDecision("לא נתמך", null, null) }), "{}");
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, plan);
        await GenerationHarness.Start(parent, draft, "GenerateQuestions");
        await app.Worker.RunNextAsync(default);
        draft = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        using var other = await app.App.ParentAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(Path(draft) + "/undo", new { expectedRevision = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(Path(draft) + "/undo", new { expectedRevision = 1 })).StatusCode);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", new
            { operationKey = Guid.NewGuid(), expectedRevision = 2, kind = "Revise", message = "בקשה" });
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            await app.Worker.RunNextAsync(default);
            var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
            Assert.Equal(2, saved["revision"]!.GetValue<long>());
            Assert.True(saved["canUndo"]!.GetValue<bool>());
            Assert.True(JsonNode.DeepEquals(draft["document"], saved["document"]));
        }
        // A deliberate no-op manual save still consumes the previous AI undo checkpoint.
        using var manual = await parent.PutAsJsonAsync(Path(draft), Edit(draft));
        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
        var after = (await manual.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(2, after["revision"]!.GetValue<long>());
        Assert.False(after["canUndo"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Chat_bounds_drop_the_oldest_completed_exchange_and_snapshots_exclude_parent_conversation()
    {
        var plan = Numeric(1);
        await using var app = new GenerationHarness(GenerationHarness.Questions(), Serialize(new { result = new RevisionDecision("תשובה", null, null) }));
        using var parent = await app.ParentAsync();
        var turns = Enumerable.Range(0, 100).Select(i => new
        { role = i % 2 == 0 ? "parent" : "assistant", text = "turn " + i, atUtc = DateTime.UtcNow }).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), plan, chat = turns.Append(turns[0]) })).StatusCode);
        using var created = await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), plan, chat = turns });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        await GenerationHarness.Start(parent, draft, "Create");
        await app.Worker.RunNextAsync(default);
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", new
        { operationKey = Guid.NewGuid(), expectedRevision = 2, kind = "Revise", message = "השאלה הנוכחית" });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        await app.Worker.RunNextAsync(default);
        var saved = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        var chat = saved["chat"]!.AsArray();
        Assert.InRange(chat.Count, 98, 100);
        Assert.DoesNotContain(chat, t => t!["text"]!.GetValue<string>() is "turn 0" or "turn 1");
        Assert.Equal("השאלה הנוכחית", chat[^2]!["text"]!.GetValue<string>());
        Assert.Equal("תשובה", chat[^1]!["text"]!.GetValue<string>());
        var payload = JsonNode.Parse(app.Chat.Requests[1].Input.Split('\n')[^1])!;
        Assert.DoesNotContain(payload["context"]!.AsArray(), t => t!["text"]!.GetValue<string>() == "השאלה הנוכחית");
        Assert.InRange(payload["context"]!.AsArray().Count, 1, 6);
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Null(snapshot["chat"]);
        Assert.Null(snapshot["undo"]);
        using var copy = await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), snapshotId = snapshot["id"]!.GetValue<Guid>() });
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);
        Assert.Empty((await copy.Content.ReadFromJsonAsync<JsonNode>())!["chat"]!.AsArray());
    }

    [Fact]
    public async Task Imported_chat_rejects_forged_operation_references_and_keeps_authoring_reply_bound()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var turn = new { role = "assistant", text = new string('א', 1000), atUtc = DateTime.UtcNow, target = (object?)null, assumptions = Array.Empty<string>() };
        using var created = await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), plan = Numeric(), chat = new[] { turn } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var forged = JsonNode.Parse(Serialize(new { id = Guid.NewGuid(), plan = Numeric(), chat = new[] { turn } }))!;
        forged["chat"]![0]!["operationId"] = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/activity-drafts", forged)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), plan = Numeric(), chat = new[] { turn with { text = new string('א', 1001) } } })).StatusCode);
    }
}
