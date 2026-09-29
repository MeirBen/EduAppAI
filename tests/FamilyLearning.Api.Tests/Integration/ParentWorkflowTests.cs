using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ParentWorkflowTests
{
    [Fact]
    public async Task Requires_authentication_and_csrf_and_logout_removes_access()
    {
        using var app = new ApiFactory();
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/templates")).StatusCode);
        using var parent = await app.ParentAsync();
        parent.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition())).StatusCode);
        await ApiFactory.RefreshCsrfAsync(parent);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await parent.GetAsync("/api/templates")).StatusCode);
    }

    [Fact]
    public async Task Saves_frozen_draft_and_keeps_old_version_after_template_edit()
    {
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(new AiFixtures.ScriptedChat(AiFixtures.Content(count: 3).ToJsonString())));
        using var parent = await app.ParentAsync();
        var empty = await parent.GetFromJsonAsync<JsonElement>("/api/templates");
        Assert.Equal(0, empty.GetArrayLength());
        var created = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        var generated = await parent.PostAsJsonAsync($"/api/templates/{id}/instances",
            new { questionCount = 3, parameters = new { theme = "חלל" } });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);
        var instance = await generated.Content.ReadFromJsonAsync<JsonElement>();
        var instanceId = instance.GetProperty("id").GetGuid();
        Assert.Equal("Draft", instance.GetProperty("status").GetString());
        Assert.Equal(3, instance.GetProperty("content").GetProperty("questions").GetArrayLength());

        var definition = AiFixtures.Definition();
        definition["name"] = "New name";
        var update = new { expectedVersion = 1, definition };
        Assert.Equal(HttpStatusCode.Created, (await parent.PostAsJsonAsync($"/api/templates/{id}/versions", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsJsonAsync($"/api/templates/{id}/versions", update)).StatusCode);
        var fetched = await parent.GetFromJsonAsync<JsonElement>($"/api/instances/{instanceId}");
        Assert.Equal(instance.GetProperty("content").GetRawText(), fetched.GetProperty("content").GetRawText());
        Assert.Equal(instance.GetProperty("createdAtUtc").GetString(), fetched.GetProperty("createdAtUtc").GetString());
        Assert.Equal(1, fetched.GetProperty("templateVersion").GetInt32());
        var listedInstances = await parent.GetFromJsonAsync<JsonElement>("/api/instances");
        Assert.Equal(instance.GetProperty("createdAtUtc").GetString(), listedInstances[0].GetProperty("createdAtUtc").GetString());
        var listedTemplates = await parent.GetFromJsonAsync<JsonElement>("/api/templates");
        Assert.EndsWith("Z", listedTemplates[0].GetProperty("createdAtUtc").GetString());
    }

    [Fact]
    public async Task Other_families_cannot_read_or_change_templates_or_drafts()
    {
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(new AiFixtures.ScriptedChat(AiFixtures.Content(count: 3).ToJsonString())));
        using var owner = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var response = await owner.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        var template = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        var draftResponse = await owner.PostAsJsonAsync($"/api/templates/{id}/instances", new { questionCount = 3, parameters = new { } });
        var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/instances/{draft.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync($"/api/templates/{id}/instances", new { questionCount = 3, parameters = new { } })).StatusCode);
        Assert.Equal(0, (await stranger.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
    }

    [Fact]
    public async Task Rejects_quoted_numbers_in_typed_json_contracts()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var definition = AiFixtures.Definition();
        definition["schemaVersion"] = "3";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await parent.PostAsJsonAsync("/api/templates", definition)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_publications_create_only_one_next_version()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var created = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        var update = new { expectedVersion = 1, definition = AiFixtures.Definition() };
        var responses = await Task.WhenAll(
            parent.PostAsJsonAsync($"/api/templates/{id}/versions", update),
            parent.PostAsJsonAsync($"/api/templates/{id}/versions", update));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var latest = await parent.GetFromJsonAsync<JsonElement>($"/api/templates/{id}");
        Assert.Equal(2, latest.GetProperty("currentVersion").GetInt32());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"x\",\"instanceParameters\":null,\"generation\":null}")]
    [InlineData("{\"schemaVersion\":1,\"name\":\"x\",\"instanceParameters\":[null],\"generation\":null}")]
    public async Task Malformed_definitions_are_client_errors(string body)
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var response = await parent.PostAsync("/api/templates", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Database_constraint_failures_are_not_reported_as_stale_edits_and_roll_back_publication()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        using var created = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        var template = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectPublication BEFORE INSERT ON TaskTemplateVersions
            BEGIN SELECT RAISE(ABORT, 'private constraint diagnostic'); END;
            """);
        using var response = await parent.PostAsJsonAsync($"/api/templates/{id}/versions",
            new { expectedVersion = 1, definition = AiFixtures.Definition() });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private constraint diagnostic", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, (await db.TaskTemplates.SingleAsync()).CurrentVersion);
        Assert.Equal(1, await db.TaskTemplateVersions.CountAsync());
    }

    [Fact]
    public async Task Invalid_instance_parameters_do_not_create_a_draft()
    {
        using var app = new ApiFactory();
        using var parent = await app.ParentAsync();
        var response = await parent.PostAsJsonAsync("/api/templates", AiFixtures.Definition());
        var template = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = template.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await parent.PostAsJsonAsync($"/api/templates/{id}/instances", new { questionCount = 0, parameters = new { } })).StatusCode);
        Assert.Equal(0, (await parent.GetFromJsonAsync<JsonElement>("/api/instances")).GetArrayLength());
        var unknown = await parent.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }
}
