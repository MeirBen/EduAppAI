using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ActivityOnlyTests
{
    [Fact]
    public async Task Retired_template_routes_return_not_found_without_creating_content()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await parent.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync("/api/templates", Numeric())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.GetAsync("/api/templates/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.PostAsJsonAsync("/api/templates/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/versions",
            new { expectedVersion = 1, definition = Numeric() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.DeleteAsync("/api/templates/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")).StatusCode);
        Assert.Empty((await parent.GetFromJsonAsync<JsonArray>("/api/activity-drafts"))!);
    }

    [Theory]
    [InlineData("templateId")]
    [InlineData("expectedVersion")]
    public async Task Retired_create_fields_are_rejected_even_when_null(string field)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var request = new JsonObject { ["id"] = Guid.NewGuid(), ["plan"] = Fixtures.AiFixtures.PlanJson(), [field] = null };
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/activity-drafts", request)).StatusCode);
        Assert.Empty((await parent.GetFromJsonAsync<JsonArray>("/api/activity-drafts"))!);
    }

    [Fact]
    public async Task Drafts_and_snapshots_have_no_template_provenance()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(parent);
        Assert.False(draft.AsObject().ContainsKey("templateVersionId"));
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release", new { expectedRevision = 2 });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        Assert.False((await release.Content.ReadFromJsonAsync<JsonObject>())!.ContainsKey("templateVersionId"));
    }

    [Theory]
    [InlineData("GenerateMaterials")]
    [InlineData("ReplaceMaterial")]
    [InlineData("ReplaceQuestion")]
    public async Task Retired_operation_kinds_cannot_admit_work(string kind)
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Reading());
        using var result = await parent.PostAsJsonAsync(Path(draft) + "/operations",
            new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var scope = app.App.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<LearningDbContext>().GenerationOperations.AnyAsync());
        Assert.Empty(app.Chat.Requests);
    }

    [Theory]
    [InlineData("targetId")]
    [InlineData("instruction")]
    public async Task Retired_operation_fields_are_rejected_even_when_null(string field)
    {
        await using var app = new GenerationHarness();
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric());
        var request = new JsonObject { ["operationKey"] = Guid.NewGuid(), ["expectedRevision"] = 1, ["kind"] = "Create", [field] = null };
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync(Path(draft) + "/operations", request)).StatusCode);
        Assert.False(await app.Worker.RunNextAsync(default));
        Assert.Empty(app.Chat.Requests);
    }
}
