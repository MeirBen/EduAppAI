using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class MigrationTests
{
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
