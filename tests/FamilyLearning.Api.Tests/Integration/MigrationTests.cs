using System.Net;
using System.Net.Http.Json;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

using static FamilyLearning.Api.Tests.Fixtures.AiFixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class MigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upgrading_existing_templates_preserves_snapshots_and_allows_publication(bool legacyAuthoringSource)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928144220_AiOnlyTemplates");
        if (legacyAuthoringSource)
        {
            // Early databases recorded this migration before it removed the retired column.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE TaskTemplateVersions ADD COLUMN AuthoringSource TEXT NOT NULL;");
        }
        var owner = await db.Users.SingleAsync();
        var template = new TaskTemplate(owner.FamilyId, "Existing AI template");
        db.TaskTemplates.Add(template);
        await db.SaveChangesAsync();
        var version = new TaskTemplateVersion(template.Id, 1, Definition().ToJsonString());
        if (legacyAuthoringSource)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO TaskTemplateVersions (Id, TemplateId, Version, DefinitionJson, CreatedAtUtc, AuthoringSource)
                VALUES ({version.Id}, {template.Id}, 1, {version.DefinitionJson}, {version.CreatedAtUtc}, 'Ai');
                """);
        }
        else
        {
            db.TaskTemplateVersions.Add(version);
            await db.SaveChangesAsync();
        }
        var instance = new TaskInstance(owner.FamilyId, version.Id, "Saved task", "{}", Content().ToJsonString(), "{}");
        db.TaskInstances.Add(instance);
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(version.DefinitionJson, (await db.TaskTemplateVersions.SingleAsync()).DefinitionJson);
        var stored = await db.TaskInstances.SingleAsync();
        Assert.Equal(instance.Id, stored.Id);
        Assert.Equal(version.Id, stored.TemplateVersionId);
        Assert.Equal(instance.ContentJson, stored.ContentJson);
        Assert.Equal(1, await db.Users.CountAsync());

        using var create = await parent.PostAsJsonAsync("/api/templates", Definition());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var revise = await parent.PostAsJsonAsync($"/api/templates/{template.Id}/versions",
            new { expectedVersion = 1, definition = Definition() });
        Assert.Equal(HttpStatusCode.Created, revise.StatusCode);
        Assert.Equal(3, await db.TaskTemplateVersions.CountAsync());
    }

    [Fact]
    public async Task Ai_transition_removes_old_learning_data_but_keeps_parent_accounts()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928143821_AddAiGenerationMetadata");
        var owner = await db.Users.SingleAsync();
        var template = new TaskTemplate(owner.FamilyId, "Old prototype");
        var version = new TaskTemplateVersion(template.Id, 1, """{"schemaVersion":1}""");
        db.TaskTemplates.Add(template);
        db.TaskTemplateVersions.Add(version);
        db.TaskInstances.Add(new TaskInstance(owner.FamilyId, version.Id, "Old draft", "{}", "{}", "{}"));
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Families.CountAsync());
        Assert.Empty(await db.TaskTemplates.ToListAsync());
        Assert.Empty(await db.TaskTemplateVersions.ToListAsync());
        Assert.Empty(await db.TaskInstances.ToListAsync());
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
