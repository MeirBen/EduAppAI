using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityOnlyCutoverTests
{
    private const string PreviousMigration = "20261009111918_RemoveUnusedActivityState";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_migration_rejects_existing_learning_data_without_deleting_it(bool templatesOnly)
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        if (templatesOnly) await AddTemplate(db);
        else await Create(parent, Numeric());
        var tables = await Tables(db);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.MigrateAsync());
        Assert.Equal(tables, await Tables(db));
        Assert.Equal(PreviousMigration, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(templatesOnly ? 0 : 1, await db.ActivityDrafts.CountAsync());
        Assert.Equal(templatesOnly ? 1 : 0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM TaskTemplates").SingleAsync());
        Assert.Single(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Explicit_cutover_clears_all_learning_data_keeps_accounts_and_cannot_reset_new_work_twice()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var otherParent = await h.App.ParentAsync();
        var profile = await ChildHarness.Create(parent);
        using var child = await h.Activate(parent, profile);
        var assignment = await AssignmentTests.Assign(parent, profile, await AssignmentTests.Snapshot(parent));
        await ChildSessionTests.Start(child, ChildSessionTests.SessionPath(assignment));
        await GenerationHarness.Start(parent, await Create(parent, Numeric()), "Create");
        await Create(otherParent, Numeric());
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var accounts = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.FamilyId, u.PasswordHash, u.SecurityStamp }).ToArrayAsync();
        var keys = Directory.GetFiles(System.IO.Path.Combine(h.App.DataDirectory, "keys")).ToDictionary(p => p, File.ReadAllText);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await AddTemplate(db);
        Assert.Equal(0, await ManagementCommand.RunAsync(h.App.Services, ["--activity-only-cutover"]));
        Assert.Equal(accounts, await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.FamilyId, u.PasswordHash, u.SecurityStamp }).ToArrayAsync());
        Assert.Equal(2, await db.Families.CountAsync());
        foreach (var key in keys) Assert.Equal(key.Value, File.ReadAllText(key.Key));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
            SELECT (SELECT COUNT(*) FROM TaskSessions) + (SELECT COUNT(*) FROM Assignments) +
                (SELECT COUNT(*) FROM ChildDeviceGrants) + (SELECT COUNT(*) FROM ChildActivations) +
                (SELECT COUNT(*) FROM Children) + (SELECT COUNT(*) FROM GenerationOperations) +
                (SELECT COUNT(*) FROM ActivityDrafts) + (SELECT COUNT(*) FROM TaskSnapshots) AS Value
            """).SingleAsync());
        Assert.DoesNotContain("TaskTemplates", await Tables(db));
        Assert.DoesNotContain("TaskTemplateVersions", await Tables(db));
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
        var fresh = await Create(parent, Numeric());
        Assert.Equal(0, await ManagementCommand.RunAsync(h.App.Services, ["--activity-only-cutover"]));
        Assert.True(JsonNode.DeepEquals(fresh, await parent.GetFromJsonAsync<JsonNode>(Path(fresh))));
    }

    [Fact]
    public async Task Failed_cutover_rolls_back_learning_deletes_and_leaves_the_old_schema()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await ChildHarness.Create(parent);
        await AssignmentTests.Snapshot(parent);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectCutover BEFORE DELETE ON TaskSnapshots BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => ManagementCommand.RunAsync(h.App.Services, ["--activity-only-cutover"]));
        Assert.Single(await db.ActivityDrafts.ToListAsync());
        Assert.Single(await db.TaskSnapshots.ToListAsync());
        Assert.Equal(profile["id"]!.GetValue<Guid>(), (await db.Children.SingleAsync()).Id);
        Assert.Equal(PreviousMigration, (await db.Database.GetAppliedMigrationsAsync()).Last());
    }

    private static Task<string[]> Tables(LearningDbContext db) => db.Database
        .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name").ToArrayAsync();

    private static async Task AddTemplate(LearningDbContext db)
    {
        var family = await db.Families.Select(f => f.Id).FirstAsync();
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TaskTemplates (Id, FamilyId, Name, CurrentVersion, CreatedAtUtc, UpdatedAtUtc) VALUES ({id}, {family}, 'old', 1, {DateTime.UtcNow}, {DateTime.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TaskTemplateVersions (Id, TemplateId, Version, DefinitionJson, CreatedAtUtc) VALUES ({Guid.NewGuid()}, {id}, 1, 'unsupported old plan', {DateTime.UtcNow})");
    }
}
