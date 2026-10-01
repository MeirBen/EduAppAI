using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class MigrationTests
{
    [Fact]
    public async Task Restarting_the_real_host_preserves_a_reviewed_snapshot_and_account()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "family-learning-restart", Guid.NewGuid().ToString());
        Guid snapshotId;
        string snapshotJson;
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
                using var scope = first.Services.CreateScope();
                snapshotJson = (await scope.ServiceProvider.GetRequiredService<LearningDbContext>()
                    .TaskSnapshots.SingleAsync()).DocumentJson;
            }
            await using var restarted = new ApiFactory(storageDirectory: directory);
            using var client = restarted.CreateClient();
            using var verification = restarted.Services.CreateScope();
            var db = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
            var account = await db.Users.SingleAsync();
            await ApiFactory.RefreshCsrfAsync(client);
            using var login = await client.PostAsJsonAsync("/api/auth/login",
                new { email = account.Email, password = "Testing!Passphrase123" });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
            Assert.Equal(snapshotJson, (await db.TaskSnapshots.SingleAsync()).DocumentJson);
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
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
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
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
