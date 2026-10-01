using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Features.Templates;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class LibraryDeletionTests
{
    [Fact]
    public async Task Family_reset_clears_drafts_beyond_the_list_limit_and_can_be_repeated()
    {
        await using var app = new ApiFactory();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(owner);
        var foreign = await ActivityReleaseTests.ReadyDraft(stranger);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            var original = await db.ActivityDrafts.SingleAsync(d => d.Id == draft["id"]!.GetValue<Guid>());
            for (var index = 0; index < 101; index++)
                db.ActivityDrafts.Add(new ActivityDraft(original.FamilyId, original.Name, original.PlanJson,
                    original.InputJson, original.DocumentJson, null, null, original.CreatedByParentId));
            await db.SaveChangesAsync();
        }
        Assert.Equal(100, (await owner.GetFromJsonAsync<JsonElement>("/api/activity-drafts")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync(ActivityDraftTests.Path(foreign))).StatusCode);
        using var verification = app.Services.CreateScope();
        var remaining = verification.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Single(await remaining.ActivityDrafts.ToListAsync());
        Assert.Equal(2, await remaining.Users.CountAsync());
    }

    [Fact]
    public async Task Content_first_template_deletion_preserves_independent_drafts_and_snapshots_and_reset_is_family_scoped()
    {
        await using var app = new ApiFactory();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var plan = TaskEngine.LearningPlanFixture.Numeric(1);
        using var published = await owner.PostAsJsonAsync("/api/templates", plan);
        var template = await published.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = template.GetProperty("id").GetGuid();
        using var copied = await owner.PostAsJsonAsync("/api/activity-drafts", new { templateId, expectedVersion = 1, input = new Api.TaskEngine.Models.TaskRequest(plan.Defaults) });
        var draft = (await copied.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>())!;
        var edit = ActivityDraftTests.Edit(draft);
        edit["document"] = ActivityDraftTests.Document();
        draft = await ActivityDraftTests.Save(owner, draft, edit);
        using var released = await owner.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        var snapshot = await released.Content.ReadFromJsonAsync<JsonElement>();
        var snapshotId = snapshot.GetProperty("id").GetGuid();
        var foreign = await ActivityReleaseTests.ReadyDraft(stranger);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/templates/{templateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(ActivityDraftTests.Path(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(ActivityDraftTests.Path(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/instances/{snapshotId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync(ActivityDraftTests.Path(foreign))).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Families.CountAsync());
        Assert.Single(await db.ActivityDrafts.ToListAsync());
        Assert.Empty(await db.TaskSnapshots.ToListAsync());
        Assert.Empty(await db.TaskTemplates.ToListAsync());
        Assert.Empty(await db.TaskTemplateVersions.ToListAsync());
    }

    [Fact]
    public async Task Content_first_reset_failure_rolls_back_draft_and_snapshot_deletion()
    {
        await using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        using var released = await parent.PostAsJsonAsync(ActivityDraftTests.Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectSnapshotDeletion BEFORE DELETE ON TaskSnapshots BEGIN SELECT RAISE(ABORT, 'private'); END;");
        }
        Assert.Equal(HttpStatusCode.InternalServerError, (await parent.DeleteAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync(ActivityDraftTests.Path(draft))).StatusCode);
        Assert.Equal(1, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_during_publication_returns_not_found_without_restoring_content(bool reset)
    {
        var publication = new PausedPublication();
        using var app = new ApiFactory(services => services.AddScoped(provider => new LearningDbContext(
            new DbContextOptionsBuilder<LearningDbContext>(provider.GetRequiredService<DbContextOptions<LearningDbContext>>())
                .AddInterceptors(publication).Options)));
        using var parent = await app.ParentAsync();
        var id = await SaveTemplateAsync(parent);
        var saving = parent.PostAsJsonAsync($"/api/templates/{id}/versions",
            new { expectedVersion = 1, definition = AiFixtures.PlanJson() });
        try
        {
            await publication.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent,
                (await parent.DeleteAsync(reset ? "/api/templates" : $"/api/templates/{id}")).StatusCode);
        }
        finally { publication.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.NotFound, (await saving).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.False(await db.TaskTemplates.AnyAsync());
        Assert.False(await db.TaskTemplateVersions.AnyAsync());
        Assert.False(await db.ActivityDrafts.AnyAsync());
    }

    private static async Task<Guid> SaveTemplateAsync(HttpClient parent)
    {
        var response = await parent.PostAsJsonAsync("/api/templates", AiFixtures.PlanJson());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private sealed class PausedPublication : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TaskTemplateVersion>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Version == 2))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
