using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.Integration.GenerationHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class ContentLifecycleTests
{
    [Fact]
    public async Task Application_host_generates_without_publication_and_freezes_an_owned_snapshot()
    {
        var chat = new AiFixtures.ScriptedChat(Questions());
        using var app = new ApiFactory(services => services.AddSingleton<IChatClient>(chat));
        using var parent = await app.ParentAsync();
        using var stranger = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var operation = await Start(parent, draft);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        do
        {
            await Task.Delay(50, deadline.Token);
            operation = (await parent.GetFromJsonAsync<JsonNode>(OperationPath(operation), deadline.Token))!;
        } while (operation["status"]!.GetValue<string>() is "queued" or "calling");
        Assert.Equal("completed", operation["status"]!.GetValue<string>());
        draft = (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
        Assert.Equal("2", draft["document"]!["questions"]![0]!["answer"]!["value"]!.GetValue<string>());
        Assert.Empty((await parent.GetFromJsonAsync<JsonArray>("/api/templates"))!);
        using var release = await parent.PostAsJsonAsync(Path(draft) + "/release",
            new { expectedRevision = draft["revision"]!.GetValue<long>() });
        Assert.Equal(HttpStatusCode.Created, release.StatusCode);
        var snapshot = (await release.Content.ReadFromJsonAsync<JsonNode>())!;
        var path = $"/api/instances/{snapshot["id"]!.GetValue<Guid>()}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await parent.DeleteAsync(Path(draft))).StatusCode);
        var persisted = await parent.GetFromJsonAsync<JsonNode>(path);
        Assert.True(JsonNode.DeepEquals(snapshot, persisted));
        Assert.Single(chat.Requests);
    }
}
