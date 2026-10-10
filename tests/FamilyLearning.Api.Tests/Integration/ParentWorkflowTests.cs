using System.Net;
using System.Net.Http.Json;
using System.Text;
using FamilyLearning.Api.Tests.Fixtures;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ParentWorkflowTests
{
    [Fact]
    public async Task Content_first_routes_share_real_auth_csrf_no_store_and_family_boundaries()
    {
        await using var app = new ApiFactory();
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await ActivityReleaseTests.ReadyDraft(owner);
        var path = ActivityDraftTests.Path(draft);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(path, ActivityDraftTests.Edit(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path + "/release", new { expectedRevision = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(path + "/adopt-content", new { expectedRevision = 2, materialIds = Array.Empty<string>(), questionIds = Array.Empty<string>() })).StatusCode);
        using var read = await owner.GetAsync(path);
        Assert.True(read.Headers.CacheControl!.NoStore);
        owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path + "/release", new { expectedRevision = 2 })).StatusCode);
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Requires_authentication_and_csrf_and_logout_removes_access()
    {
        using var app = new ApiFactory();
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/activity-drafts")).StatusCode);
        using var parent = await app.ParentAsync();
        parent.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), plan = AiFixtures.PlanJson() })).StatusCode);
        await ApiFactory.RefreshCsrfAsync(parent);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await parent.GetAsync("/api/activity-drafts")).StatusCode);
    }

    [Fact]
    public async Task Rejects_quoted_numbers_in_typed_json_contracts()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = AiFixtures.PlanJson();
        definition["schemaVersion"] = "3";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await parent.PostAsJsonAsync("/api/activity-drafts", new { id = Guid.NewGuid(), plan = definition })).StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"name\":null,\"materials\":null}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"x\",\"materials\":[null],\"questions\":null}")]
    public async Task Malformed_definitions_are_client_errors(string body)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var response = await parent.PostAsync("/api/activity-drafts", new StringContent($"{{\"id\":\"{Guid.NewGuid()}\",\"plan\":{body}}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_API_paths_return_safe_problems()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var unknown = await parent.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }
}
