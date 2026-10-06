using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.AssignmentTests;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;
using static FamilyLearning.Api.Tests.Integration.ChildSessionTests;
using static FamilyLearning.Api.Tests.Integration.ParentReviewTests;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class SessionRaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_parents_cannot_replace_a_review_and_identical_replay_keeps_the_original_reviewer(bool changed)
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        await Submit(child, assignment, content.Answers());
        using var secondParent = h.App.CreateClient();
        string firstParentId;
        using (var scope = h.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var first = await db.Users.SingleAsync();
            firstParentId = first.Id;
            var email = $"second-{Guid.NewGuid():N}@example.test";
            var second = new ParentUser { UserName = email, Email = email, FamilyId = first.FamilyId };
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ParentUser>>().CreateAsync(second, "Testing!Passphrase123")).Succeeded);
            await ApiFactory.RefreshCsrfAsync(secondParent);
            Assert.Equal(HttpStatusCode.NoContent, (await secondParent.PostAsJsonAsync("/api/auth/login", new { email, password = "Testing!Passphrase123" })).StatusCode);
            await ApiFactory.RefreshCsrfAsync(secondParent);
        }
        pause.Arm();
        var waiting = secondParent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(content.TextId, changed ? 1 : 2)]));
        JsonNode original;
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var winner = await parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(content.TextId, 2)]));
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
            original = (await winner.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.Equal(firstParentId, original["reviewedByParentId"]!.GetValue<string>());
            h.Clock.Now = h.Clock.Now.AddHours(1);
        }
        finally { pause.Resume.TrySetResult(); }
        using var loser = await waiting;
        Assert.Equal(changed ? HttpStatusCode.Conflict : HttpStatusCode.OK, loser.StatusCode);
        if (!changed) Assert.True(JsonNode.DeepEquals(original, await loser.Content.ReadFromJsonAsync<JsonNode>()));
        Assert.True(JsonNode.DeepEquals(original, await secondParent.GetFromJsonAsync<JsonNode>(ResultPath(assignment))));
        using var verification = h.App.Services.CreateScope();
        var stored = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(3, (await stored.Assignments.SingleAsync()).Revision);
        Assert.Equal(3, (await stored.TaskSessions.SingleAsync()).Revision);
        Assert.Equal(firstParentId, (await stored.TaskSessions.SingleAsync()).ReviewedByParentId);
    }

    [Fact]
    public async Task Review_failure_rolls_back_completion_and_a_lost_acknowledgement_recovers_the_saved_report()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var childBefore = await Submit(child, assignment, content.Answers());
        var before = await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment));
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_review BEFORE UPDATE ON TaskSessions WHEN NEW.ReviewedAtUtc IS NOT NULL BEGIN SELECT RAISE(ABORT, 'isolated failure'); END;");
        var request = new FinalizeReviewRequest(2, [new(content.TextId, 2)]);
        Assert.Equal(HttpStatusCode.InternalServerError, (await parent.PostAsJsonAsync(ReviewPath(assignment), request)).StatusCode);
        Assert.True(JsonNode.DeepEquals(before, await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment))));
        Assert.True(JsonNode.DeepEquals(childBefore, await child.GetFromJsonAsync<JsonNode>(SessionPath(assignment))));
        Assert.Equal("awaiting-review", (await db.Assignments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(2, (await db.Assignments.AsNoTracking().SingleAsync()).Revision);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_review;");
        // Ignore a committed response: GET and an explicit replay must recover the original report, never grade twice.
        (await parent.PostAsJsonAsync(ReviewPath(assignment), request)).Dispose();
        var recovered = await parent.GetFromJsonAsync<JsonNode>(ResultPath(assignment));
        Assert.Equal(9, recovered!["evaluation"]!["finalTotal"]!.GetValue<int>());
        h.Clock.Now = h.Clock.Now.AddHours(1);
        using var replay = await parent.PostAsJsonAsync(ReviewPath(assignment), request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(recovered, await replay.Content.ReadFromJsonAsync<JsonNode>()));
    }

    [Fact]
    public async Task Reset_committed_before_review_cannot_recreate_a_deleted_result()
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        await Submit(child, assignment, content.Answers());
        pause.Arm();
        var waiting = parent.PostAsJsonAsync(ReviewPath(assignment), new FinalizeReviewRequest(2, [new(content.TextId, 2)]));
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync("/api/templates")).StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.NotFound, (await waiting).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.GetAsync(ResultPath(assignment))).StatusCode);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Empty(await db.TaskSessions.ToListAsync());
        Assert.Empty(await db.Assignments.ToListAsync());
    }

    [Theory]
    [InlineData("save", "revoke")]
    [InlineData("submit", "disable")]
    [InlineData("submit", "reset")]
    [InlineData("submit", "expire")]
    [InlineData("start", "reset")]
    public async Task A_write_authenticated_before_access_loss_rechecks_current_access_inside_its_transaction(string operation, string loss)
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var path = SessionPath(await Assign(parent, profile, content.Id));
        if (operation != "start") await Start(child, path);
        pause.Arm();
        var writing = operation switch
        {
            "save" => child.PutAsJsonAsync(path, new SaveAnswersRequest(1, content.Answers())),
            "start" => child.PostAsync(path, null),
            _ => child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, content.Answers()))
        };
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (loss == "reset") Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync("/api/templates")).StatusCode);
            else if (loss == "disable")
                Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), new { name = "disabled", enabled = false, expectedRevision = 1 })).StatusCode);
            else if (loss == "revoke")
            {
                var devices = (await parent.GetFromJsonAsync<JsonNode>(Path(profile) + "/devices"))!;
                var grantId = devices["items"]![0]!["id"]!.GetValue<Guid>();
                Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(Path(profile) + "/devices/" + grantId)).StatusCode);
            }
            else h.Clock.Now = h.Clock.Now.AddDays(30);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await writing).StatusCode);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        if (loss == "reset")
        {
            Assert.Empty(await db.TaskSessions.ToListAsync());
            Assert.Empty(await db.Assignments.ToListAsync());
        }
        else
        {
            var session = await db.TaskSessions.SingleAsync();
            Assert.Equal(1, session.Revision);
            Assert.Equal("[]", session.AnswersJson);
            Assert.Null(session.EvaluationJson);
            Assert.Null(session.SubmittedAtUtc);
            Assert.Equal("assigned", (await db.Assignments.SingleAsync()).Status);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submission_and_withdrawal_have_one_winner_without_partial_state(bool submissionFirst)
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var assignment = await Assign(parent, profile, content.Id);
        var path = SessionPath(assignment);
        await Start(child, path);
        var submit = new SubmitAnswersRequest(1, content.Answers());
        var withdrawPath = $"/api/assignments/{assignment["id"]}/withdraw";
        pause.Arm();
        var waiting = submissionFirst ? parent.PostAsJsonAsync(withdrawPath, new { expectedRevision = 1 }) : child.PostAsJsonAsync(path + "/submit", submit);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var first = submissionFirst ? await child.PostAsJsonAsync(path + "/submit", submit) : await parent.PostAsJsonAsync(withdrawPath, new { expectedRevision = 1 });
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }
        finally { pause.Resume.TrySetResult(); }
        Assert.Equal(submissionFirst ? HttpStatusCode.Conflict : HttpStatusCode.Gone, (await waiting).StatusCode);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var session = await db.TaskSessions.SingleAsync();
        var storedAssignment = await db.Assignments.SingleAsync();
        Assert.Equal(submissionFirst ? "awaiting-review" : "withdrawn", storedAssignment.Status);
        Assert.Equal(2, storedAssignment.Revision);
        Assert.Equal(submissionFirst ? 2 : 1, session.Revision);
        Assert.Equal(submissionFirst, session.EvaluationJson is not null);
        Assert.Equal(submissionFirst, session.SubmittedAtUtc is not null);
        Assert.Equal(submissionFirst ? 3 : 0, StoredJson.Read<SessionAnswer[]>(session.AnswersJson).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_submits_cannot_change_a_frozen_result_and_identical_replay_keeps_metadata(bool changed)
    {
        var pause = new PausedChildWrite();
        await using var h = new ChildHarness(pause.Configure);
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var path = SessionPath(await Assign(parent, profile, content.Id));
        await Start(child, path);
        pause.Arm();
        var waiting = child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, content.Answers(changed ? "different" : "first")));
        JsonNode outcome;
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var first = await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, content.Answers("first")));
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            outcome = (await first.Content.ReadFromJsonAsync<JsonNode>())!;
            h.Clock.Now = h.Clock.Now.AddMinutes(1);
        }
        finally { pause.Resume.TrySetResult(); }
        using var second = await waiting;
        Assert.Equal(changed ? HttpStatusCode.Conflict : HttpStatusCode.OK, second.StatusCode);
        if (!changed) Assert.True(JsonNode.DeepEquals(outcome, await second.Content.ReadFromJsonAsync<JsonNode>()));
        Assert.True(JsonNode.DeepEquals(outcome, await child.GetFromJsonAsync<JsonNode>(path)));
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(2, (await db.TaskSessions.SingleAsync()).Revision);
        Assert.Equal(2, (await db.Assignments.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Failed_submission_rolls_back_answers_evaluation_and_assignment_then_retry_commits_once()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var path = SessionPath(await Assign(parent, profile, content.Id));
        var before = await Start(child, path);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_session BEFORE UPDATE ON TaskSessions BEGIN SELECT RAISE(ABORT, 'isolated failure'); END;");
        var request = new SubmitAnswersRequest(1, content.Answers());
        Assert.Equal(HttpStatusCode.InternalServerError, (await child.PostAsJsonAsync(path + "/submit", request)).StatusCode);
        Assert.True(JsonNode.DeepEquals(before, await child.GetFromJsonAsync<JsonNode>(path)));
        Assert.Equal("assigned", (await db.Assignments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, (await db.Assignments.AsNoTracking().SingleAsync()).Revision);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_session;");
        // Discard the successful response, as if its acknowledgement was lost; read/replay recovers the persisted outcome.
        (await child.PostAsJsonAsync(path + "/submit", request)).Dispose();
        var recovered = await child.GetFromJsonAsync<JsonNode>(path);
        Assert.Equal("awaiting-review", recovered!["status"]!.GetValue<string>());
        using var replay = await child.PostAsJsonAsync(path + "/submit", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(JsonNode.DeepEquals(recovered, await replay.Content.ReadFromJsonAsync<JsonNode>()));
    }
}
