using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.AssignmentTests;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ChildSessionTests
{
    internal sealed record SessionContent(Guid Id, string NumberId, string ChoiceId, string TextId)
    {
        internal SessionAnswer[] Answers(string text = "  שָׁלוֹם\nעולם  ") =>
            [new(NumberId, "+02.00"), new(ChoiceId, "כן"), new(TextId, text)];
    }
    internal static string SessionPath(JsonNode assignment) => $"/api/child/assignments/{assignment["id"]!.GetValue<Guid>()}/session";

    internal static async Task<SessionContent> MixedSnapshot(HttpClient parent, int textPoints = 3)
    {
        var draft = await ActivityDraftTests.Create(parent, Mixed());
        var edit = ActivityDraftTests.Edit(draft);
        edit["document"] = JsonSerializer.SerializeToNode(new
        {
            title = "תרגול",
            materials = Array.Empty<object>(),
            questions = new object[]
            {
                new { id = (string?)null, prompt = "מספר?", interaction = new { type = "numeric-input" }, answer = new { value = "2" }, points = 5 },
                new { id = (string?)null, prompt = "בחירה?", interaction = new { type = "single-choice", options = new[] { "כן", "לא", "אולי" } }, answer = new { value = "כן" }, points = 2 },
                new { id = (string?)null, prompt = "טקסט?", interaction = new { type = "text-input" }, answer = new { value = "private-parent-key" }, points = textPoints }
            }
        });
        draft = await ActivityDraftTests.Save(parent, draft, edit);
        using var release = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.True(release.StatusCode == HttpStatusCode.Created, await release.Content.ReadAsStringAsync());
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        var questions = snapshot["document"]!["questions"]!;
        return new(snapshot["id"]!.GetValue<Guid>(), questions[0]!["id"]!.GetValue<string>(),
            questions[1]!["id"]!.GetValue<string>(), questions[2]!["id"]!.GetValue<string>());
    }

    internal static async Task<JsonNode> Start(HttpClient child, string path)
    {
        using var response = await child.PostAsync(path, null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Fact]
    public async Task Parent_timing_uses_first_start_and_submission_including_breaks_but_excluding_review()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var foreign = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var detailPath = $"/api/assignments/{assignment["id"]}";
        var path = SessionPath(assignment);
        var detail = (await parent.GetFromJsonAsync<JsonNode>(detailPath))!;
        Assert.True(detail.AsObject().ContainsKey("startedAtUtc"));
        Assert.Null(detail["startedAtUtc"]);
        Assert.Null(detail["submittedAtUtc"]);
        Assert.Equal(HttpStatusCode.NotFound, (await child.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(detailPath)).StatusCode);
        var started = h.Clock.Now.UtcDateTime;
        await Start(child, path);
        h.Clock.Now = h.Clock.Now.AddMinutes(65);
        await Start(child, path);
        Assert.Equal(HttpStatusCode.OK, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, content.Answers()))).StatusCode);
        detail = (await parent.GetFromJsonAsync<JsonNode>(detailPath))!;
        Assert.Equal(started, detail["startedAtUtc"]!.GetValue<DateTime>());
        Assert.Equal(h.Clock.Now.UtcDateTime, detail["savedAtUtc"]!.GetValue<DateTime>());
        Assert.Null(detail["submittedAtUtc"]);
        h.Clock.Now = h.Clock.Now.AddMinutes(2);
        var submitted = h.Clock.Now.UtcDateTime;
        Assert.Equal(HttpStatusCode.OK, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, content.Answers()))).StatusCode);
        h.Clock.Now = h.Clock.Now.AddDays(1);
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(ParentReviewTests.ReviewPath(assignment),
            new FinalizeReviewRequest(3, [new(content.TextId, 2)]))).StatusCode);
        detail = (await parent.GetFromJsonAsync<JsonNode>(detailPath))!;
        var result = (await parent.GetFromJsonAsync<JsonNode>(ParentReviewTests.ResultPath(assignment)))!;
        foreach (var response in new[] { detail, result })
        {
            Assert.Equal(started, response["startedAtUtc"]!.GetValue<DateTime>());
            Assert.Equal(submitted, response["submittedAtUtc"]!.GetValue<DateTime>());
        }
    }

    [Fact]
    public async Task Starts_are_idempotent_reads_do_not_create_work_and_saves_only_advance_session_revision()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var path = SessionPath(assignment);
        Assert.Equal(HttpStatusCode.NotFound, (await child.GetAsync(path)).StatusCode);
        Assert.False((await parent.GetFromJsonAsync<JsonNode>("/api/assignments"))!["items"]![0]!["hasStarted"]!.GetValue<bool>());
        var starts = await Task.WhenAll(child.PostAsync(path, null), child.PostAsync(path, null));
        Assert.Single(starts, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(starts, r => r.StatusCode == HttpStatusCode.OK);
        var start = (await starts[0].Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.True(JsonNode.DeepEquals(start, await starts[1].Content.ReadFromJsonAsync<JsonNode>()));
        Properties(start, "assignmentId", "revision", "status", "answers", "startedAtUtc", "savedAtUtc", "submittedAtUtc", "reviewedAtUtc", "finalTotal", "possibleTotal");
        Assert.Equal(1, start["revision"]!.GetValue<long>());
        Assert.Null(start["savedAtUtc"]);
        Assert.Null(start["finalTotal"]);
        Assert.Empty(start["answers"]!.AsArray());
        SessionAnswer[] buffer = [new(content.NumberId, "-"), new(content.TextId, "  שָׁלוֹם\n  ")];
        using var save = await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, buffer));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = (await save.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(2, saved["revision"]!.GetValue<long>());
        Assert.Equal("assigned", saved["status"]!.GetValue<string>());
        Assert.Equal(buffer[1].Value, saved["answers"]![1]!["value"]!.GetValue<string>());
        Properties(saved["answers"]![0]!, "questionId", "value");
        Assert.True(JsonNode.DeepEquals(saved, await child.GetFromJsonAsync<JsonNode>(path)));
        Assert.Equal(HttpStatusCode.Conflict, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, []))).StatusCode);
        var summary = (await parent.GetFromJsonAsync<JsonNode>("/api/assignments"))!["items"]![0]!;
        Assert.True(summary["hasStarted"]!.GetValue<bool>());
        Assert.Equal(1, summary["revision"]!.GetValue<long>());
        Assert.True((await child.GetFromJsonAsync<JsonNode>("/api/child/assignments"))!["items"]![0]!["hasStarted"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("  שָׁלוֹם\nעולם  ", "awaiting-review", null)]
    [InlineData("", "completed", 7)]
    public async Task Submit_freezes_the_final_buffer_and_replay_returns_identical_outcome(string text, string status, int? finalTotal)
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var path = SessionPath(assignment);
        await Start(child, path);
        var answers = content.Answers(text);
        using var submit = await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, answers));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var outcome = (await submit.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(status, outcome["status"]!.GetValue<string>());
        Assert.Equal(2, outcome["revision"]!.GetValue<long>());
        Assert.Equal(finalTotal, outcome["finalTotal"]?.GetValue<int>());
        Assert.Equal(10, outcome["possibleTotal"]!.GetValue<int>());
        Properties(outcome, "assignmentId", "revision", "status", "answers", "startedAtUtc", "savedAtUtc", "submittedAtUtc", "reviewedAtUtc", "finalTotal", "possibleTotal");
        foreach (var answer in outcome["answers"]!.AsArray()) Properties(answer!, "questionId", "value");
        Assert.DoesNotContain("private-parent-key", outcome.ToJsonString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", outcome["submittedAtUtc"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(outcome, await child.GetFromJsonAsync<JsonNode>(path)));
        Assert.True(JsonNode.DeepEquals(outcome, await Start(child, path)));
        var replayAnswers = answers.Where(a => !string.IsNullOrWhiteSpace(a.Value)).Reverse().ToArray();
        using var replay = await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, replayAnswers));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(outcome, await replay.Content.ReadFromJsonAsync<JsonNode>()));
        Assert.Equal(HttpStatusCode.Conflict, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, content.Answers("different")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(2, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync($"/api/assignments/{assignment["id"]}/withdraw", new { expectedRevision = 2 })).StatusCode);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(2, (await db.Assignments.SingleAsync()).Revision);
        var stored = await db.Database.SqlQueryRaw<string>("SELECT EvaluationJson AS Value FROM TaskSessions").SingleAsync();
        var evaluation = StoredJson.Read<SessionEvaluation>(stored);
        Assert.Equal(7, evaluation.AutomaticSubtotal);
        Assert.Equal(finalTotal, evaluation.FinalTotal);
        Assert.Equal(status == "awaiting-review" ? 1 : 0, evaluation.PendingCount);
    }

    [Fact]
    public async Task Raw_validation_precedes_replay_and_invalid_writes_leave_saved_state_unchanged()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var path = SessionPath(await Assign(parent, profile, content.Id));
        await Start(child, path);
        var invalidBodies = new object[]
        {
            new { expectedRevision = 1 }, new { answers = Array.Empty<object>() }, new { expectedRevision = 0, answers = Array.Empty<object>() },
            new { expectedRevision = 1, answers = (object?)null }, new { expectedRevision = 1, answers = new object?[] { null } },
            new { expectedRevision = 1, answers = new[] { new { questionId = content.NumberId } } },
            new { expectedRevision = 1, answers = new[] { new { value = "2" } } },
            new { expectedRevision = 1, answers = new[] { new { questionId = (string?)null, value = "2" } } },
            new { expectedRevision = 1, answers = new[] { new { questionId = content.NumberId, value = (string?)null } } },
            new { expectedRevision = 1, answers = new[] { new { questionId = content.NumberId, value = "2", points = 5 } } },
            new { expectedRevision = 1, answers = new[] { new { questionId = "unknown", value = "2" } } },
            new { expectedRevision = 1, answers = new[] { new { questionId = "", value = "2" } } },
            new { expectedRevision = 1, answers = new[] { new { questionId = content.NumberId, value = new string(' ', 201) } } },
            new { expectedRevision = 1, answers = Enumerable.Repeat(new { questionId = content.NumberId, value = "2" }, 2).ToArray() },
            new { expectedRevision = 1, answers = Enumerable.Repeat(new { questionId = content.NumberId, value = "2" }, 21).ToArray() }
        };
        foreach (var terminal in new[] { false, true })
        {
            if (terminal) Assert.Equal(HttpStatusCode.OK, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, []))).StatusCode);
            var before = await child.GetFromJsonAsync<JsonNode>(path);
            foreach (var body in invalidBodies)
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsJsonAsync(path + "/submit", body)).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await child.PutAsJsonAsync(path, body)).StatusCode);
            }
            Assert.True(JsonNode.DeepEquals(before, await child.GetFromJsonAsync<JsonNode>(path)));
        }
        using var malformed = await child.PostAsync(path + "/submit", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var replay = await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, [new(content.TextId, " \t")]));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task Invalid_choices_and_unfinished_numbers_do_not_submit_and_replay_compares_preserved_strings()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent, textPoints: 0);
        var path = SessionPath(await Assign(parent, profile, content.Id));
        await Start(child, path);
        SessionAnswer[] unfinished = [new(content.NumberId, "1."), new(content.TextId, new string('א', 200))];
        Assert.Equal(HttpStatusCode.OK, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, unfinished))).StatusCode);
        var saved = await child.GetFromJsonAsync<JsonNode>(path);
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, unfinished))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(2, [new(content.ChoiceId, "unknown")]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, content.Answers()))).StatusCode);
        Assert.True(JsonNode.DeepEquals(saved, await child.GetFromJsonAsync<JsonNode>(path)));
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{content.Id}")).StatusCode);
        using var submit = await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, content.Answers()));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var outcome = (await submit.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("awaiting-review", outcome["status"]!.GetValue<string>());
        Assert.Null(outcome["finalTotal"]);
        Assert.Equal(7, outcome["possibleTotal"]!.GetValue<int>());
        var equivalentNumber = content.Answers();
        equivalentNumber[0] = new(content.NumberId, "2");
        Assert.Equal(HttpStatusCode.Conflict, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, equivalentNumber))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, content.Answers("שָׁלוֹם\nעולם")))).StatusCode);
        Assert.True(JsonNode.DeepEquals(outcome, await child.GetFromJsonAsync<JsonNode>(path)));
    }

    [Fact]
    public async Task Session_access_is_bound_to_the_child_and_active_assignment_with_identity_bound_csrf()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var otherParent = await h.App.ParentAsync();
        var profile = await Create(parent);
        var siblingProfile = await Create(parent);
        var foreignProfile = await Create(otherParent);
        using var child = await h.Activate(parent, profile);
        using var sibling = await h.Activate(parent, siblingProfile);
        using var foreign = await h.Activate(otherParent, foreignProfile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var path = SessionPath(assignment);
        var siblingPath = SessionPath(await Assign(parent, siblingProfile, content.Id));
        await Start(sibling, siblingPath);
        await Start(child, path);
        foreach (var client in new[] { sibling, foreign, parent })
        {
            var expected = client == parent ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound;
            Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(expected, (await client.PostAsync(path, null)).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync(path, new SaveAnswersRequest(1, []))).StatusCode);
            Assert.Equal(expected, (await client.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, []))).StatusCode);
        }
        child.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, []))).StatusCode);
        child.DefaultRequestHeaders.Add("X-XSRF-TOKEN", sibling.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single());
        Assert.Equal(HttpStatusCode.BadRequest, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, []))).StatusCode);
        await Csrf(child);
        Assert.Equal(HttpStatusCode.OK, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, content.Answers()))).StatusCode);
        Assert.Empty((await sibling.GetFromJsonAsync<JsonNode>(siblingPath))!["answers"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync($"/api/assignments/{assignment["id"]}/withdraw", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await child.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await child.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await child.PutAsJsonAsync(path, new SaveAnswersRequest(2, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(2, []))).StatusCode);
    }
}
