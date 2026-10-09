using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ChildSessionTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class MigrationTests
{
    [Fact]
    public async Task Activity_storage_has_one_plan_authority_without_override_columns()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var draftColumns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ActivityDrafts')").ToArrayAsync();
        var snapshotColumns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('TaskSnapshots')").ToArrayAsync();
        var operationColumns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('GenerationOperations')").ToArrayAsync();
        Assert.DoesNotContain("InputFingerprint", operationColumns);
        Assert.Contains("PlanJson", draftColumns);
        Assert.Contains("PlanJson", snapshotColumns);
        Assert.DoesNotContain("InputJson", draftColumns);
        Assert.DoesNotContain("InputJson", snapshotColumns);
        Assert.DoesNotContain("ResolvedInputJson", snapshotColumns);
    }

    [Fact]
    public async Task Upgrade_from_profile_details_adds_update_timestamp_without_rewriting_profiles()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var created = await parent.PostAsJsonAsync("/api/children", new { name = "ילד", details = new { grade = "גן חובה", age = 5 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var earlier = db.Database.GetMigrations().Single(m => m.EndsWith("_AddChildProfileDetails", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(earlier);
        var columns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Children')").ToArrayAsync();
        Assert.Contains("AgeConfirmedAtUtc", columns);
        Assert.DoesNotContain("UpdatedAtUtc", columns);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.True(JsonNode.DeepEquals(profile, (await parent.GetFromJsonAsync<JsonNode>("/api/children"))!["items"]![0]));
    }

    [Fact]
    public async Task Adding_profile_details_preserves_existing_saved_work_and_content()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await ChildHarness.Create(parent);
        using var child = await h.Activate(parent, profile);
        var content = await MixedSnapshot(parent);
        var path = SessionPath(await AssignmentTests.Assign(parent, profile, content.Id));
        await Start(child, path);
        using var save = await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, content.Answers()));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = await save.Content.ReadFromJsonAsync<JsonNode>();
        var snapshot = await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{content.Id}");
        profile = (await parent.GetFromJsonAsync<JsonNode>("/api/children"))!["items"]![0]!;
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var previous = db.Database.GetMigrations().Single(m => m.EndsWith("_AddTaskSessions", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.True(JsonNode.DeepEquals(saved, await child.GetFromJsonAsync<JsonNode>(path)));
        Assert.True(JsonNode.DeepEquals(snapshot, await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{content.Id}")));
        Assert.True(JsonNode.DeepEquals(profile, (await parent.GetFromJsonAsync<JsonNode>("/api/children"))!["items"]![0]));
    }

    [Fact]
    public async Task Adding_sessions_preserves_existing_assignments_content_and_grants()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await ChildHarness.Create(parent);
        using var child = await h.Activate(parent, profile);
        var snapshot = await AssignmentTests.Snapshot(parent);
        var assignment = await AssignmentTests.Assign(parent, profile, snapshot);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var document = (await db.TaskSnapshots.AsNoTracking().SingleAsync()).DocumentJson;
        var previous = db.Database.GetMigrations().Single(m => m.EndsWith("_AddAssignments", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(document, (await db.TaskSnapshots.AsNoTracking().SingleAsync()).DocumentJson);
        Assert.Single(await db.ChildDeviceGrants.ToListAsync());
        var migratedChild = await db.Children.AsNoTracking().SingleAsync();
        Assert.Null(migratedChild.Grade);
        Assert.Null(migratedChild.Age);
        Assert.Null(migratedChild.AgeConfirmedAtUtc);
        Assert.Null(migratedChild.UpdatedAtUtc);
        Assert.Single(await db.Assignments.ToListAsync());
        Assert.Empty(await db.TaskSessions.ToListAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await child.GetAsync(SessionPath(assignment))).StatusCode);
        Assert.Equal("assigned", (await Start(child, SessionPath(assignment)))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Restart_preserves_saved_pending_and_completed_sessions_and_frozen_scoring_without_AI()
    {
        var directory = Path.Combine(Path.GetTempPath(), "family-learning-sessions", Guid.NewGuid().ToString());
        var chat = new AiFixtures.ScriptedChat();
        var responses = new Dictionary<string, JsonNode>();
        (Guid Id, string Answers, string? Evaluation, int? Policy, long Revision)[] persisted;
        string childCookie;
        JsonNode childProfile;
        try
        {
            await using (var first = new ApiFactory(s => s.AddSingleton<IChatClient>(chat), storageDirectory: directory))
            {
                using var parent = await first.ParentAsync();
                var profile = await ChildHarness.Create(parent);
                using var profileUpdate = await parent.PutAsJsonAsync(ChildHarness.Path(profile),
                    new { name = "ילד", enabled = true, expectedRevision = 1, details = new { grade = "כיתה ד׳", age = 9 } });
                Assert.Equal(HttpStatusCode.OK, profileUpdate.StatusCode);
                childProfile = (await profileUpdate.Content.ReadFromJsonAsync<JsonNode>())!;
                var code = await ChildHarness.Issue(parent, profile);
                using var child = first.CreateClient();
                await ChildHarness.Csrf(child);
                using var activation = await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() });
                Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
                childCookie = activation.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("FamilyLearning.Child=", StringComparison.Ordinal)).Split(';')[0];
                await ChildHarness.Csrf(child);
                foreach (var state in new[] { "assigned", "awaiting-review", "completed" })
                {
                    var content = await MixedSnapshot(parent);
                    var path = SessionPath(await AssignmentTests.Assign(parent, profile, content.Id));
                    await Start(child, path);
                    using var write = state == "assigned"
                        ? await child.PutAsJsonAsync(path, new SaveAnswersRequest(1, [new(content.NumberId, "-"), new(content.TextId, "  שָׁלוֹם  ")]))
                        : await child.PostAsJsonAsync(path + "/submit", new SubmitAnswersRequest(1, content.Answers(state == "completed" ? "" : "  שָׁלוֹם  ")));
                    Assert.Equal(HttpStatusCode.OK, write.StatusCode);
                    var response = (await write.Content.ReadFromJsonAsync<JsonNode>())!;
                    Assert.Equal(state, response["status"]!.GetValue<string>());
                    responses.Add(path, response);
                }
                using var scope = first.Services.CreateScope();
                var sessions = await scope.ServiceProvider.GetRequiredService<LearningDbContext>().TaskSessions.AsNoTracking().OrderBy(s => s.AssignmentId).ToArrayAsync();
                persisted = sessions.Select(s => (s.AssignmentId, s.AnswersJson, s.EvaluationJson, s.ScoringPolicyVersion, s.Revision)).ToArray();
                Assert.Single(sessions, s => s.ScoringPolicyVersion is null);
                Assert.Equal(2, sessions.Count(s => s.ScoringPolicyVersion == 1));
                Assert.All(sessions, s => Assert.Equal(DateTimeKind.Utc, s.StartedAtUtc.Kind));
                Assert.Equal(7, StoredJson.Read<SessionEvaluation>(sessions.Single(s => s.EvaluationJson is not null && StoredJson.Read<SessionEvaluation>(s.EvaluationJson).PendingCount == 0).EvaluationJson!).FinalTotal);
            }
            await using var restarted = new ApiFactory(s => s.AddSingleton<IChatClient>(chat), storageDirectory: directory);
            using var resumed = restarted.CreateClient();
            resumed.DefaultRequestHeaders.Add("Cookie", childCookie);
            foreach (var (path, response) in responses)
                Assert.True(JsonNode.DeepEquals(response, await resumed.GetFromJsonAsync<JsonNode>(path)));
            using var verification = restarted.Services.CreateScope();
            var stored = await verification.ServiceProvider.GetRequiredService<LearningDbContext>().TaskSessions.AsNoTracking().OrderBy(s => s.AssignmentId).ToArrayAsync();
            var restoredProfile = await verification.ServiceProvider.GetRequiredService<LearningDbContext>().Children.AsNoTracking().SingleAsync();
            Assert.Equal(childProfile["grade"]!.GetValue<string>(), restoredProfile.Grade);
            Assert.Equal(childProfile["age"]!.GetValue<int>(), restoredProfile.Age);
            Assert.Equal(childProfile["ageConfirmedAtUtc"]!.GetValue<DateTime>(), restoredProfile.AgeConfirmedAtUtc);
            Assert.Equal(DateTimeKind.Utc, restoredProfile.AgeConfirmedAtUtc!.Value.Kind);
            Assert.Equal(childProfile["updatedAtUtc"]!.GetValue<DateTime>(), restoredProfile.UpdatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, restoredProfile.UpdatedAtUtc!.Value.Kind);
            Assert.Equal(persisted, stored.Select(s => (s.AssignmentId, s.AnswersJson, s.EvaluationJson, s.ScoringPolicyVersion, s.Revision)).ToArray());
            Assert.Empty(chat.Requests);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additive_upgrades_preserve_existing_snapshot_json_and_device_access(bool withChildAccess)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        await AssignmentTests.Snapshot(parent);
        if (withChildAccess)
        {
            var profile = await ChildHarness.Create(parent);
            var code = await ChildHarness.Issue(parent, profile);
            using var child = app.CreateClient();
            await ChildHarness.Csrf(child);
            Assert.Equal(HttpStatusCode.NoContent, (await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() })).StatusCode);
        }
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var before = await db.TaskSnapshots.AsNoTracking().SingleAsync();
        var earlier = db.Database.GetMigrations().Single(m => m.EndsWith(withChildAccess ? "_AddChildAccess" : "_InitialCreate", StringComparison.Ordinal));
        // Only this disposable test database is downgraded to construct the previously shipped schema.
        await db.GetService<IMigrator>().MigrateAsync(earlier);
        Assert.Equal(before.DocumentJson, await db.Database.SqlQueryRaw<string>("SELECT DocumentJson AS Value FROM TaskSnapshots").SingleAsync());
        // Upgrade a consistent disposable copy, including committed WAL data, never a user's database.
        var copyPath = Path.Combine(app.DataDirectory, "upgrade-copy.db");
        await db.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {copyPath}");
        await using var upgraded = new LearningDbContext(new DbContextOptionsBuilder<LearningDbContext>()
            .UseSqlite($"Data Source={copyPath};Pooling=False").Options);
        Assert.Equal(earlier, (await upgraded.Database.GetAppliedMigrationsAsync()).Last());
        await upgraded.Database.MigrateAsync();
        await upgraded.Database.MigrateAsync();
        var after = await upgraded.TaskSnapshots.AsNoTracking().SingleAsync();
        Assert.Equal((before.PlanJson, before.DocumentJson, before.MeasurementsJson),
            (after.PlanJson, after.DocumentJson, after.MeasurementsJson));
        Assert.Single(await upgraded.Users.ToListAsync());
        var upgradedDraft = Assert.Single(await upgraded.ActivityDrafts.ToListAsync());
        Assert.Equal("[]", upgradedDraft.ChatJson);
        Assert.Null(upgradedDraft.UndoJson);
        Assert.Equal(withChildAccess ? 1 : 0, await upgraded.ChildDeviceGrants.CountAsync());
        Assert.Equal(upgraded.Database.GetMigrations(), await upgraded.Database.GetAppliedMigrationsAsync());
        Assert.Equal(earlier, (await db.Database.GetAppliedMigrationsAsync()).Last());
    }

    [Fact]
    public async Task Restarting_the_real_host_preserves_a_reviewed_snapshot_and_account()
    {
        var directory = Path.Combine(Path.GetTempPath(), "family-learning-restart", Guid.NewGuid().ToString());
        Guid snapshotId;
        string snapshotJson;
        string childCookie;
        JsonNode childIdentity;
        try
        {
            await using (var first = new ApiFactory(storageDirectory: directory))
            {
                using var parent = await first.ParentAsync();
                var draft = await ActivityReleaseTests.ReadyDraft(parent);
                using var release = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release",
                    new { expectedRevision = 2 });
                Assert.Equal(HttpStatusCode.Created, release.StatusCode);
                snapshotId = (await release.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();
                var profile = await ChildHarness.Create(parent);
                var code = await ChildHarness.Issue(parent, profile);
                using var child = first.CreateClient();
                await ChildHarness.Csrf(child);
                using var activation = await child.PostAsJsonAsync("/api/child/auth/activate", new { code = code["code"]!.GetValue<string>() });
                Assert.Equal(HttpStatusCode.NoContent, activation.StatusCode);
                childCookie = activation.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("FamilyLearning.Child=", StringComparison.Ordinal)).Split(';')[0];
                childIdentity = (await child.GetFromJsonAsync<JsonNode>("/api/child/auth/me"))!;
                using var scope = first.Services.CreateScope();
                snapshotJson = (await scope.ServiceProvider.GetRequiredService<LearningDbContext>()
                    .TaskSnapshots.SingleAsync()).DocumentJson;
            }
            await using var restarted = new ApiFactory(storageDirectory: directory);
            using var client = restarted.CreateClient();
            using var childAfterRestart = restarted.CreateClient();
            childAfterRestart.DefaultRequestHeaders.Add("Cookie", childCookie);
            Assert.True(JsonNode.DeepEquals(childIdentity, await childAfterRestart.GetFromJsonAsync<JsonNode>("/api/child/auth/me")));
            using var verification = restarted.Services.CreateScope();
            var db = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
            var account = await db.Users.SingleAsync();
            await ApiFactory.RefreshCsrfAsync(client);
            using var login = await client.PostAsJsonAsync("/api/auth/login",
                new { email = account.Email, password = "Testing!Passphrase123" });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
            Assert.Equal(snapshotJson, (await db.TaskSnapshots.SingleAsync()).DocumentJson);
            Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Fresh_installation_and_repeated_migration_preserve_accounts_and_content_without_old_tables()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        using var publication = await parent.PostAsJsonAsync("/api/templates", Numeric());
        Assert.Equal(HttpStatusCode.Created, publication.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Families.ToListAsync());
        Assert.Single(await db.TaskTemplates.ToListAsync());
        Assert.True(JsonNode.DeepEquals(draft, await parent.GetFromJsonAsync<JsonNode>(ActivityDraftTests.Path(draft))));
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToArrayAsync();
        Assert.Contains("ActivityDrafts", tables);
        Assert.Contains("TaskSnapshots", tables);
        Assert.Contains("GenerationOperations", tables);
        Assert.DoesNotContain("TaskInstances", tables);
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public void Current_model_matches_the_checked_in_migration()
    {
        using var app = new ApiFactory();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
