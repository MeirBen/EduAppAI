using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ChildHarness;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ChildProfileTests
{
    [Fact]
    public async Task Deletion_and_assignment_creation_serialize_without_losing_history()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var profile = await Create(parent);
        var snapshotId = await AssignmentTests.Snapshot(parent);
        var responses = await Task.WhenAll(parent.DeleteAsync(Path(profile) + "?expectedRevision=1"),
            parent.PostAsJsonAsync("/api/assignments", new { childId = profile["id"]!.GetValue<Guid>(), snapshotId }));
        if (responses[0].StatusCode == HttpStatusCode.NoContent)
            Assert.Equal(HttpStatusCode.NotFound, responses[1].StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
        }
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Equal(await db.Children.CountAsync(), await db.Assignments.CountAsync());
    }

    [Fact]
    public async Task Deletion_requires_ownership_current_revision_and_no_assignment_history()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var foreign = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        await Issue(parent, profile);
        var path = Path(profile) + "?expectedRevision=1";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.DeleteAsync(Path(profile) + "?expectedRevision=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await parent.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.GetAsync("/api/child/auth/me")).StatusCode);
        using var scope = h.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        Assert.Empty(await db.Children.ToListAsync());
        Assert.Empty(await db.ChildActivations.ToListAsync());
        Assert.Empty(await db.ChildDeviceGrants.ToListAsync());

        profile = await Create(parent);
        var assignment = await AssignmentTests.Assign(parent, profile, await AssignmentTests.Snapshot(parent));
        Assert.True((await parent.GetFromJsonAsync<JsonNode>("/api/children"))!["items"]![0]!["hasAssignments"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Conflict, (await parent.DeleteAsync(Path(profile) + "?expectedRevision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsJsonAsync($"/api/assignments/{assignment["id"]}/withdraw", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.DeleteAsync(Path(profile) + "?expectedRevision=1")).StatusCode);
        Assert.Single(await db.Children.ToListAsync());
        Assert.Single(await db.Assignments.ToListAsync());
    }

    [Fact]
    public async Task Optional_details_clear_independently_and_unrelated_edits_preserve_age_timestamp()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var empty = await Create(parent);
        Assert.Null(empty["updatedAtUtc"]);
        Assert.True(empty.AsObject().ContainsKey("grade"));
        Assert.Null(empty["grade"]);
        Assert.Null(empty["age"]);
        Assert.Null(empty["ageConfirmedAtUtc"]);
        using var created = await parent.PostAsJsonAsync("/api/children", new { name = "נועה", details = new { grade = "  כיתה ד׳  ", age = 9 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        var path = Path(profile);
        Assert.Equal("כיתה ד׳", profile["grade"]!.GetValue<string>());
        Assert.Equal(9, profile["age"]!.GetValue<int>());
        var confirmed = profile["ageConfirmedAtUtc"]!.GetValue<DateTime>();
        Assert.Equal(h.Clock.Now.UtcDateTime, confirmed);
        h.Clock.Now = h.Clock.Now.AddDays(1);
        // Older clients omit details: renaming or disabling must not erase educational context.
        using var renamed = await parent.PutAsJsonAsync(path, new { name = "שם חדש", enabled = true, expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        profile = (await renamed.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("כיתה ד׳", profile["grade"]!.GetValue<string>());
        Assert.Equal(9, profile["age"]!.GetValue<int>());
        Assert.Equal(confirmed, profile["ageConfirmedAtUtc"]!.GetValue<DateTime>());
        Assert.Equal(h.Clock.Now.UtcDateTime, profile["updatedAtUtc"]!.GetValue<DateTime>());
        Assert.Equal(empty["createdAtUtc"]!.GetValue<DateTime>(), profile["createdAtUtc"]!.GetValue<DateTime>());
        using var clearedGrade = await parent.PutAsJsonAsync(path, new { name = "שם חדש", enabled = true, expectedRevision = 2, details = new { grade = "  ", age = 9 } });
        Assert.Equal(HttpStatusCode.OK, clearedGrade.StatusCode);
        profile = (await clearedGrade.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Null(profile["grade"]);
        Assert.Equal(confirmed, profile["ageConfirmedAtUtc"]!.GetValue<DateTime>());
        using var clearedAge = await parent.PutAsJsonAsync(path, new { name = "שם חדש", enabled = true, expectedRevision = 3, details = new { grade = "גן חובה", age = (int?)null } });
        Assert.Equal(HttpStatusCode.OK, clearedAge.StatusCode);
        profile = (await clearedAge.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal("גן חובה", profile["grade"]!.GetValue<string>());
        Assert.Null(profile["age"]);
        Assert.Null(profile["ageConfirmedAtUtc"]);
        var listed = (await parent.GetFromJsonAsync<JsonNode>("/api/children"))!["items"]!.AsArray().Single(c => c!["id"]!.GetValue<Guid>() == profile["id"]!.GetValue<Guid>());
        Assert.True(JsonNode.DeepEquals(profile, listed));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(121, 1)]
    [InlineData(9, 101)]
    public async Task Invalid_details_do_not_create_or_change_profiles(int age, int gradeLength)
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        var details = new { grade = new string('x', gradeLength), age };
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/children", new { name = "child", details })).StatusCode);
        var profile = await Create(parent);
        Assert.Equal(HttpStatusCode.BadRequest, (await parent.PutAsJsonAsync(Path(profile), new { name = "changed", enabled = false, expectedRevision = 1, details })).StatusCode);
        Assert.True(JsonNode.DeepEquals(profile, (await parent.GetFromJsonAsync<JsonNode>("/api/children"))!["items"]![0]));
    }

    [Fact]
    public async Task Detail_shape_is_required_and_age_boundaries_are_supported()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        foreach (var details in new object[] { new { age = 9 }, new { grade = "גן" }, new { grade = "גן", age = 9.5 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await parent.PostAsJsonAsync("/api/children", new { name = "ילד", details })).StatusCode);
        using var created = await parent.PostAsJsonAsync("/api/children", new { name = "ילד", details = new { grade = new string('א', 100), age = 0 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        h.Clock.Now = h.Clock.Now.AddDays(1);
        using var updated = await parent.PutAsJsonAsync(Path(profile), new { name = "ילד", enabled = true, expectedRevision = 1, details = new { grade = (string?)null, age = 120 } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        profile = (await updated.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(120, profile["age"]!.GetValue<int>());
        Assert.Equal(h.Clock.Now.UtcDateTime, profile["ageConfirmedAtUtc"]!.GetValue<DateTime>());
    }

    [Fact]
    public async Task Details_keep_ownership_revisions_child_allowlists_and_frozen_content_intact()
    {
        await using var h = new ChildHarness();
        using var parent = await h.App.ParentAsync();
        using var foreign = await h.App.ParentAsync();
        var profile = await Create(parent);
        using var child = await h.Activate(parent, profile);
        var snapshot = await AssignmentTests.Snapshot(parent);
        var document = await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{snapshot}");
        var request = new { name = "child", enabled = true, expectedRevision = 1, details = new { grade = "כיתה ה׳", age = 10 } };
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PutAsJsonAsync(Path(profile), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await child.PutAsJsonAsync(Path(profile), request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PutAsJsonAsync(Path(profile), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PutAsJsonAsync(Path(profile), request)).StatusCode);
        var identity = (await child.GetFromJsonAsync<JsonNode>("/api/child/auth/me"))!;
        AssignmentTests.Properties(identity, "childId", "name", "expiresAtUtc", "answerLength");
        Assert.True(JsonNode.DeepEquals(document, await parent.GetFromJsonAsync<JsonNode>($"/api/instances/{snapshot}")));
        using var scope = h.App.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<LearningDbContext>().GenerationOperations.ToListAsync());
    }
}
