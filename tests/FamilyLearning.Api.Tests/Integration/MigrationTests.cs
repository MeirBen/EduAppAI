using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class MigrationTests
{
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
        var document = (await db.TaskSnapshots.AsNoTracking().SingleAsync()).DocumentJson;
        var earlier = db.Database.GetMigrations().Single(m => m.EndsWith(withChildAccess ? "_AddChildAccess" : "_InitialCreate", StringComparison.Ordinal));
        // Only this disposable test database is downgraded to construct the previously shipped schema.
        await db.GetService<IMigrator>().MigrateAsync(earlier);
        Assert.Equal(document, await db.Database.SqlQueryRaw<string>("SELECT DocumentJson AS Value FROM TaskSnapshots").SingleAsync());
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(document, (await db.TaskSnapshots.AsNoTracking().SingleAsync()).DocumentJson);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Equal(withChildAccess ? 1 : 0, await db.ChildDeviceGrants.CountAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
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
