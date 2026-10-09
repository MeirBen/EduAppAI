using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.AssignmentTests;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;
using static FamilyLearning.Api.Tests.Integration.ChildSessionTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ParentReviewTests
{
    [Fact]
    public async Task Archived_content_and_disabled_child_keep_the_same_report_after_a_real_host_restart()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "family-learning-review", Guid.NewGuid().ToString());
        var chat = new AiFixtures.ScriptedChat();
        JsonNode report;
        string path;
        string parentEmail;
        string answersJson;
        string evaluationJson;
        FinalizeReviewRequest request;
        string reviewPath;
        try
        {
            await using (var first = new ApiFactory(s => s.AddSingleton<IChatClient>(chat), storageDirectory: directory))
            {
                using var parent = await first.ParentAsync();
                parentEmail = (await parent.GetFromJsonAsync<JsonNode>("/api/auth/me"))!["email"]!.GetValue<string>();
                var profile = await Create(parent);
                var code = await Issue(parent, profile);
                using var child = first.CreateClient();
                await Csrf(child);
                Assert.Equal(HttpStatusCode.NoContent, (await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() })).StatusCode);
                await Csrf(child);
                var content = await MixedSnapshot(parent);
                var assignment = await Assign(parent, profile, content.Id);
                await Submit(child, assignment, content.Answers());
                path = ResultPath(assignment);
                reviewPath = ReviewPath(assignment);
                var pending = (await parent.GetFromJsonAsync<JsonNode>(path))!;
                Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync($"/api/instances/{content.Id}")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "ילד", enabled = false, expectedRevision = 1 })).StatusCode);
                Assert.True(JsonNode.DeepEquals(pending, await parent.GetFromJsonAsync<JsonNode>(path)));
                request = new FinalizeReviewRequest(2, [new(content.TextId, 3)]);
                using var finalized = await parent.PostAsJsonAsync(reviewPath, request);
                Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
                report = (await finalized.Content.ReadFromJsonAsync<JsonNode>())!;
                Assert.Equal(10, report["evaluation"]!["finalTotal"]!.GetValue<int>());
                Assert.True(JsonNode.DeepEquals(pending["document"], report["document"]));
                Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync(SessionPath(assignment))).StatusCode);
                using var scope = first.Services.CreateScope();
                var session = await scope.ServiceProvider.GetRequiredService<LearningDbContext>().TaskSessions.SingleAsync();
                answersJson = session.AnswersJson;
                evaluationJson = session.EvaluationJson!;
            }
            await using var restarted = new ApiFactory(s => s.AddSingleton<IChatClient>(chat), storageDirectory: directory);
            using var resumed = restarted.CreateClient();
            await ApiFactory.RefreshCsrfAsync(resumed);
            Assert.Equal(HttpStatusCode.NoContent, (await resumed.PostAsJsonAsync("/api/auth/login", new { email = parentEmail, password = "Testing!Passphrase123" })).StatusCode);
            await ApiFactory.RefreshCsrfAsync(resumed);
            Assert.True(JsonNode.DeepEquals(report, await resumed.GetFromJsonAsync<JsonNode>(path)));
            using var replay = await resumed.PostAsJsonAsync(reviewPath, request);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True(JsonNode.DeepEquals(report, await replay.Content.ReadFromJsonAsync<JsonNode>()));
            using var verification = restarted.Services.CreateScope();
            var stored = await verification.ServiceProvider.GetRequiredService<LearningDbContext>().TaskSessions.SingleAsync();
            Assert.Equal(answersJson, stored.AnswersJson);
            Assert.Equal(evaluationJson, stored.EvaluationJson);
            Assert.Equal(1, stored.ScoringPolicyVersion);
            Assert.Equal(3, stored.Revision);
            Assert.Empty(chat.Requests);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    internal static string ResultPath(JsonNode assignment) => $"/api/assignments/{assignment["id"]}/result";
    internal static string ReviewPath(JsonNode assignment) => $"/api/assignments/{assignment["id"]}/review";

    internal static async Task<JsonNode> Submit(HttpClient child, JsonNode assignment, SessionAnswer[] answers)
    {
        var path = SessionPath(assignment);
        await Start(child, path);
        using var submitted = await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, answers));
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return (await submitted.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Fact]
    public async Task Partial_credit_completes_frozen_results_once_and_child_responses_remain_allowlisted()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var submitted = await Submit(child, assignment, content.Answers());
        using var pendingResponse = await parent.GetAsync(ResultPath(assignment));
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        Assert.True(pendingResponse.Headers.CacheControl?.NoStore);
        var pending = (await pendingResponse.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("awaiting-review", pending["assignment"]!["status"]!.GetValue<string>());
        Assert.Equal(2, pending["revision"]!.GetValue<long>());
        Assert.Equal(1, pending["scoringPolicyVersion"]!.GetValue<int>());
        Assert.Equal(7, pending["evaluation"]!["automaticSubtotal"]!.GetValue<int>());
        Assert.Equal(1, pending["evaluation"]!["pendingCount"]!.GetValue<int>());
        Assert.Null(pending["evaluation"]!["finalTotal"]);
        Assert.Null(pending["reviewedByParentId"]);
        Assert.Equal("private-parent-key", pending["document"]!["questions"]![2]!["answer"]!["value"]!.GetValue<string>());
        Assert.Equal(content.Answers()[2].Value, pending["answers"]![2]!["value"]!.GetValue<string>());
        var request = new FinalizeReviewRequest(2, [new(content.TextId, 2)]);
        h.Clock.Now = h.Clock.Now.AddHours(1);
        using var finalized = await parent.PostAsJsonAsync(ReviewPath(assignment), request);
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        var result = (await finalized.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(3, result["revision"]!.GetValue<long>());
        Assert.Equal(3, result["assignment"]!["revision"]!.GetValue<long>());
        Assert.Equal("completed", result["assignment"]!["status"]!.GetValue<string>());
        Assert.Equal(9, result["evaluation"]!["finalTotal"]!.GetValue<int>());
        Assert.Equal(10, result["evaluation"]!["possibleTotal"]!.GetValue<int>());
        Assert.Equal(0, result["evaluation"]!["pendingCount"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(pending["evaluation"]!["questions"]![0], result["evaluation"]!["questions"]![0]));
        Assert.True(JsonNode.DeepEquals(pending["evaluation"]!["questions"]![1], result["evaluation"]!["questions"]![1]));
        Assert.True(JsonNode.DeepEquals(pending["answers"], result["answers"]));
        Assert.True(JsonNode.DeepEquals(pending["document"], result["document"]));
        Assert.Equal(pending["submittedAtUtc"]!.GetValue<string>(), result["submittedAtUtc"]!.GetValue<string>());
        Assert.Equal(h.Clock.Now.UtcDateTime, result["reviewedAtUtc"]!.GetValue<DateTime>());
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            Assert.Equal((await db.Users.SingleAsync()).Id, result["reviewedByParentId"]!.GetValue<string>());
            Assert.Equal(DateTimeKind.Utc, (await db.TaskSessions.SingleAsync()).ReviewedAtUtc!.Value.Kind);
        }
        h.Clock.Now = h.Clock.Now.AddHours(1);
        using var replay = await parent.PostAsJsonAsync(ReviewPath(assignment), request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(result, await replay.Content.ReadFromJsonAsync<JsonNode>()));
        Assert.True(JsonNode.DeepEquals(result, await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment))));
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(3, [new(content.TextId, 1)]))).StatusCode);
        var childResult = (await child.GetFromJsonAsync<JsonNode>(SessionPath(assignment)))!;
        Properties(childResult, "assignmentId", "revision", "status", "answers", "startedAtUtc", "savedAtUtc", "submittedAtUtc", "reviewedAtUtc", "finalTotal", "possibleTotal");
        foreach (var answer in childResult["answers"]!.AsArray()) Properties(answer!, "questionId", "value");
        Assert.Equal(9, childResult["finalTotal"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(submitted["answers"], childResult["answers"]));
        var inbox = (await child.GetFromJsonAsync<JsonNode>("/api/child/assignments?state=submitted"))!;
        Properties(inbox, "items", "page", "pageSize", "hasMore");
        Properties(inbox["items"]![0]!, "id", "title", "status", "revision", "createdAtUtc", "hasStarted");
        var learner = (await child.GetFromJsonAsync<JsonNode>($"/api/child/assignments/{assignment["id"]}"))!;
        Properties(learner, "id", "status", "revision", "createdAtUtc", "document");
        Properties(learner["document"]!, "title", "instructions", "materials", "questions");
        foreach (var question in learner["document"]!["questions"]!.AsArray())
        {
            Properties(question!, "id", "prompt", "interaction", "points");
            Properties(question!["interaction"]!, "type", "options");
        }
    }

    [Fact]
    public async Task Ownership_parent_policy_and_csrf_protect_reports_and_grading()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var foreign = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        await Submit(child, assignment, content.Answers());
        var request = new FinalizeReviewRequest(2, [new(content.TextId, 2)]);
        foreach (var client in new[] { foreign, child })
        {
            var expected = client == child ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound;
            using var read = await client.GetAsync(ResultPath(assignment));
            using var write = await client.PostAsJsonAsync(ReviewPath(assignment), request);
            Assert.Equal(expected, read.StatusCode);
            Assert.Equal(expected, write.StatusCode);
            Assert.DoesNotContain("private-parent-key", await read.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.DoesNotContain("private-parent-key", await write.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await parent.GetAsync($"/api/assignments/{Guid.NewGuid()}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync($"/api/assignments/{Guid.NewGuid()}/review", request)).StatusCode);
        parent.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(ReviewPath(assignment), request)).StatusCode);
        parent.DefaultRequestHeaders.Add("X-XSRF-TOKEN", child.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single());
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(ReviewPath(assignment), request)).StatusCode);
        using var scope = h.App.Services.CreateScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<LearningDbContext>().TaskSessions.SingleAsync()).ReviewedAtUtc);
    }

    [Fact]
    public async Task Unsubmitted_withdrawn_and_automatically_completed_work_cannot_be_graded()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var request = new FinalizeReviewRequest(1, []);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.GetAsync(ResultPath(assignment))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(ReviewPath(assignment), request)).StatusCode);
        await Start(child, SessionPath(assignment));
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(ReviewPath(assignment), request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await child.PostAsJsonAsync(SessionPath(assignment) + "/submit", new SubmitAnswersRequest(1, content.Answers("")))).StatusCode);
        var automatic = await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment));
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, []))).StatusCode);
        Assert.True(JsonNode.DeepEquals(automatic, await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment))));
        Assert.Null(automatic!["reviewedByParentId"]);
        Assert.Null(automatic["reviewedAtUtc"]);
        var withdrawn = await Assign(parent, profile, await Snapshot(parent));
        await Start(child, SessionPath(withdrawn));
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync($"/api/assignments/{withdrawn["id"]}/withdraw", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(ReviewPath(withdrawn), request)).StatusCode);
    }

    [Fact]
    public async Task Malformed_missing_duplicate_automatic_and_out_of_range_grades_never_bypass_validation_on_replay()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        await Submit(child, assignment, content.Answers());
        var invalid = new object[]
        {
            new { expectedRevision = 2 }, new { grades = Array.Empty<object>() }, new { expectedRevision = 0, grades = Array.Empty<object>() },
            new { expectedRevision = 2, grades = (object?)null }, new { expectedRevision = 2, grades = new object?[] { null } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId } } },
            new { expectedRevision = 2, grades = new[] { new { points = 1 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = (string?)null, points = 1 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = new string('a', 33), points = 1 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = 1.5 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = (int?)null } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = "1" } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = -1 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = 4 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = 1, comment = "extra" } } },
            new { expectedRevision = 2, grades = Array.Empty<object>() },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.NumberId, points = 1 } } },
            new { expectedRevision = 2, grades = new[] { new { questionId = Guid.NewGuid().ToString("N"), points = 1 } } },
            new { expectedRevision = 2, grades = Enumerable.Repeat(new { questionId = content.TextId, points = 1 }, 2).ToArray() },
            new { expectedRevision = 2, grades = Enumerable.Repeat(new { questionId = content.TextId, points = 1 }, 21).ToArray() },
            new { expectedRevision = 2, grades = new[] { new { questionId = content.TextId, points = 1 } }, finalTotal = 99 }
        };
        foreach (var terminal in new[] { false, true })
        {
            if (terminal)
                Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(content.TextId, 1)]))).StatusCode);
            var before = await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment));
            foreach (var body in invalid)
            {
                using var rejected = await parent.PostAsJsonAsync(ReviewPath(assignment), body);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.DoesNotContain("private-parent-key", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            Assert.True(JsonNode.DeepEquals(before, await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment))));
        }
    }

    [Fact]
    public async Task Zero_point_review_requires_every_answered_text_once_and_replays_reordered_grades()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var plan = Numeric(3) with { Questions = Reading().Questions };
        var draft = await ActivityDraftTests.Create(parent, plan);
        var edit = ActivityDraftTests.Edit(draft);
        edit["document"] = JsonSerializer.SerializeToNode(new
        {
            title = "טקסט",
            materials = Array.Empty<object>(),
            questions = Enumerable.Range(0, 3).Select(i => new
            { id = (string?)null, prompt = "שאלה " + i, interaction = new { type = "text-input" }, answer = new { value = "private-key" }, points = 0 }).ToArray()
        });
        draft = await ActivityDraftTests.Seed(parent, draft, edit);
        using var release = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        var questions = snapshot["document"]!["questions"]!.AsArray();
        var first = questions[0]!["id"]!.GetValue<string>();
        var second = questions[1]!["id"]!.GetValue<string>();
        var blank = questions[2]!["id"]!.GetValue<string>();
        var assignment = await Assign(parent, profile, snapshot["id"]!.GetValue<Guid>());
        await Submit(child, assignment, [new(first, "  שָׁלוֹם  "), new(second, "חלופה נכונה"), new(blank, " \t")]);
        var pending = (await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment)))!;
        Assert.Null(pending["evaluation"]!["finalTotal"]);
        Assert.Equal(2, pending["evaluation"]!["pendingCount"]!.GetValue<int>());
        Assert.Null(pending["evaluation"]!["percentage"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(first, 0)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(first, 0), new(second, 0), new(blank, 0)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(1, [new(first, 0), new(second, 0)]))).StatusCode);
        using var accepted = await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(first, 0), new(second, 0)]));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = (await accepted.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(0, result["evaluation"]!["finalTotal"]!.GetValue<int>());
        Assert.Equal(0, result["evaluation"]!["possibleTotal"]!.GetValue<int>());
        Assert.Null(result["evaluation"]!["percentage"]);
        h.Clock.Now = h.Clock.Now.AddHours(1);
        using var replay = await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(second, 0), new(first, 0)]));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(result, await replay.Content.ReadFromJsonAsync<JsonNode>()));
        var receipt = (await child.GetFromJsonAsync<JsonNode>(SessionPath(assignment)))!;
        Assert.Equal(0, receipt["finalTotal"]!.GetValue<int>());
        Assert.Null(receipt["percentage"]);
    }
}
