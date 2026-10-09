using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;

namespace FamilyLearning.Api.Tests.Integration;

internal sealed class GenerationHarness(params string[] responses) : IAsyncDisposable
{
    private ApiFactory? app;
    internal string? StorageDirectory { get; init; }
    internal ApiFactory App => app ?? throw new InvalidOperationException("Create a parent before accessing the test host.");
    internal AiFixtures.ScriptedChat Chat { get; init; } = new(responses);
    internal TestClock Clock { get; } = new();
    internal IConfiguration Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Ai:Model"] = "isolated", ["Ai:RequestTimeoutSeconds"] = "1" }).Build();
    internal GenerationWorker Worker => App.Services.GetRequiredService<GenerationWorker>();

    internal Task<HttpClient> ParentAsync(Action<IServiceCollection>? configure = null)
    {
        app ??= new ApiFactory(services =>
        {
            services.AddSingleton<IChatClient>(Chat);
            services.AddTaskAi(Configuration, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment());
            services.AddSingleton<TimeProvider>(Clock);
            // Advancing retention time must not expire the real test login session.
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = TimeProvider.System);
            services.AddActivityGeneration(Configuration);
            // Deterministic tests drive the same worker transitions without its polling loop.
            services.RemoveAll<IHostedService>();
            configure?.Invoke(services);
        }, storageDirectory: StorageDirectory);
        return App.ParentAsync();
    }

    /// <summary>Starts an explicit activity operation, defaulting to atomic Create.</summary>
    internal static async Task<JsonNode> Start(HttpClient parent, JsonNode draft, string kind = "Create")
    {
        using var response = await parent.PostAsJsonAsync(Path(draft) + "/operations", new
        { operationKey = Guid.NewGuid(), expectedRevision = draft["revision"]!.GetValue<long>(), kind });
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    /// <summary>Creates all missing activity content in one operation.</summary>
    internal async Task<JsonNode> GenerateAsync(HttpClient parent, JsonNode draft)
    {
        await Start(parent, draft);
        while (await Worker.RunNextAsync(default)) { }
        return (await parent.GetFromJsonAsync<JsonNode>(Path(draft)))!;
    }

    internal static string OperationPath(JsonNode operation) => $"/api/activity-drafts/{operation["draftId"]!.GetValue<Guid>()}/operations/{operation["id"]!.GetValue<Guid>()}";
    internal static string Questions(string type = "numeric-input") => $$"""{"title":"תרגול","instructions":"ענו","questions":[{"prompt":"כמה הם 1 ועוד 1?","interaction":{"type":"{{type}}","options":null},"answer":{"value":"2"},"points":1}]}""";
    internal const string Ideas = """
        {"ideas":[
            {"idea":{"premise":"ילדה מוצאת מכתב בגינה","structure":"גילוי, חיפוש וסיום"},"recentOverlap":0},
            {"idea":{"premise":"חברים מכינים ארוחה","structure":"תכנון וביצוע משותף"},"recentOverlap":50},
            {"idea":{"premise":"משפחה מטיילת ביער","structure":"תיאור מסלול ותחנות"},"recentOverlap":50},
            {"idea":{"premise":"שכן מטפל בציפור","structure":"בעיה, טיפול והחלמה"},"recentOverlap":50},
            {"idea":{"premise":"תלמידה בונה דגם","structure":"ניסוי, תיקון ותוצאה"},"recentOverlap":50}
        ]}
        """;
    internal const string Materials = """{"materials":[{"id":"11111111111111111111111111111111","title":null,"body":"שלום עולם"}]}""";
    /// <summary>A polish that finds nothing to change returns the written text.</summary>
    internal const string UnchangedPolish = Materials;
    public ValueTask DisposeAsync() => app?.DisposeAsync() ?? ValueTask.CompletedTask;

    internal sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
