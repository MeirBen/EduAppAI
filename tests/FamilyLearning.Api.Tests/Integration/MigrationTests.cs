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
    [Fact]
    public async Task Shared_settings_reset_learning_content_preserve_accounts_and_allow_new_tasks()
    {
        using var app = new ApiFactory(services => services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(new ScriptedChat(Content().ToJsonString())));
        using var parent = await app.ParentAsync();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928171557_RemoveLegacyTemplateAuthoringSource");
        var owner = await db.Users.SingleAsync();
        var template = new TaskTemplate(owner.FamilyId, "Old template");
        var version = new TaskTemplateVersion(template.Id, 1, """{"schemaVersion":3}""");
        db.TaskTemplates.Add(template);
        db.TaskTemplateVersions.Add(version);
        await db.SaveChangesAsync();
        var instance = new TaskInstance(owner.FamilyId, version.Id, "Old draft", "{}", "{}", "{}");
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO TaskInstances (Id, FamilyId, TemplateVersionId, Title, ParametersJson, ContentJson, GenerationMetadataJson, CreatedAtUtc, Status)
            VALUES ({{instance.Id}}, {{owner.FamilyId}}, {{version.Id}}, {{instance.Title}}, '{}', '{}', '{}', {{instance.CreatedAtUtc}}, {{instance.Status}});
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Families.CountAsync());
        Assert.Empty(await db.TaskTemplates.ToListAsync());
        Assert.Empty(await db.TaskTemplateVersions.ToListAsync());
        Assert.Empty(await db.TaskInstances.ToListAsync());
        using var create = await parent.PostAsJsonAsync("/api/templates", Definition());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var saved = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var generate = await parent.PostAsJsonAsync($"/api/templates/{saved.GetProperty("id").GetGuid()}/instances", Input());
        Assert.Equal(HttpStatusCode.Created, generate.StatusCode);
        Assert.Single(await db.TaskInstances.ToListAsync());
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
