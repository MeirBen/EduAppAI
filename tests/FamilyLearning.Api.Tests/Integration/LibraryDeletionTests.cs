using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class LibraryDeletionTests
{
    [Theory]
    [InlineData("/api/templates")]
    [InlineData("/api/templates/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/api/instances/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async Task Deletion_requires_authentication_and_csrf(string path)
    {
        using var app = new ApiFactory();
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(path)).StatusCode);
        using var parent = await app.ParentAsync();
        parent.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Draft_deletion_is_owned_and_preserves_its_template_and_other_drafts()
    {
        using var app = WithAi();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var templateId = await SaveTemplateAsync(owner);
        var draftId = await GenerateAsync(owner, templateId);
        var otherDraftId = await GenerateAsync(owner, templateId);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/instances/{draftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/instances/{draftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/instances/{draftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/instances/{draftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/templates/{templateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/instances/{otherDraftId}")).StatusCode);
    }

    [Fact]
    public async Task Template_deletion_removes_all_its_revisions_and_drafts_only()
    {
        using var app = WithAi();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var id = await SaveTemplateAsync(owner);
        var firstDraft = await GenerateAsync(owner, id);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/templates/{id}/versions",
            new { expectedVersion = 1, definition = AiFixtures.Definition() })).StatusCode);
        var secondDraft = await GenerateAsync(owner, id);
        var keptId = await SaveTemplateAsync(owner);
        var foreignId = await SaveTemplateAsync(stranger);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/instances/{firstDraft}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/instances/{secondDraft}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/templates/{keptId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync($"/api/templates/{foreignId}")).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.False(await db.TaskTemplateVersions.AnyAsync(v => v.TemplateId == id));
    }

    [Fact]
    public async Task Reset_clears_the_entire_family_library_beyond_list_limits_and_keeps_accounts_and_other_families()
    {
        using var app = WithAi();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var id = await SaveTemplateAsync(owner);
        var foreignId = await SaveTemplateAsync(stranger);
        var foreignDraftId = await GenerateAsync(stranger, foreignId);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var familyId = (await db.TaskTemplates.SingleAsync(t => t.Id == id)).FamilyId;
            for (var i = 0; i < 101; i++)
            {
                var template = new TaskTemplate(familyId, $"Saved template {i}");
                var version = new TaskTemplateVersion(template.Id, 1, AiFixtures.Definition().ToJsonString());
                db.AddRange(template, version, new TaskInstance(familyId, version.Id, "Saved draft", "{}",
                    AiFixtures.Content().ToJsonString(), "{}"));
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(100, (await owner.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/templates")).GetArrayLength());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync($"/api/instances/{foreignDraftId}")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            Assert.Equal(foreignId, (await db.TaskTemplates.SingleAsync()).Id);
            Assert.Equal(foreignId, (await db.TaskTemplateVersions.SingleAsync()).TemplateId);
            Assert.Equal(foreignDraftId, (await db.TaskInstances.SingleAsync()).Id);
            Assert.Equal(2, await db.Users.CountAsync());
            Assert.Equal(2, await db.Families.CountAsync());
        }
        Assert.True((await owner.GetFromJsonAsync<JsonElement>("/api/ai/status")).GetProperty("configured").GetBoolean());
        await SaveTemplateAsync(owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_deletion_rolls_back_drafts_and_revisions(bool reset)
    {
        using var app = WithAi();
        using var parent = await app.ParentAsync();
        var id = await SaveTemplateAsync(parent);
        var draftId = await GenerateAsync(parent, id);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER RejectVersionDeletion BEFORE DELETE ON TaskTemplateVersions
                BEGIN SELECT RAISE(ABORT, 'Test deletion failure'); END;
                """);
        }
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await parent.DeleteAsync(reset ? "/api/templates" : $"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/instances/{draftId}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_during_generation_does_not_restore_deleted_data(bool reset)
    {
        using var chat = new PausedChat();
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        var id = await SaveTemplateAsync(parent);
        var generation = parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });
        try
        {
            await chat.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(reset ? "/api/templates" : $"/api/templates/{id}")).StatusCode);
        }
        finally { chat.Resume.TrySetResult(); }
        var result = await generation;
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
    }

    private static ApiFactory WithAi() => new(services => services.AddSingleton<IChatClient>(
        new AiFixtures.ScriptedChat(AiFixtures.Content().ToJsonString(), AiFixtures.Content().ToJsonString())));

    private static async Task<Guid> SaveTemplateAsync(HttpClient parent)
    {
        var response = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> GenerateAsync(HttpClient parent, Guid id)
    {
        var response = await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { parameters = new { } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private sealed class PausedChat() : DelegatingChatClient(new AiFixtures.ScriptedChat(AiFixtures.Content().ToJsonString()))
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
    }
}
