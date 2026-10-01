using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Api.Tests.TaskEngine;
using FamilyLearning.Evaluation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class EvaluationConfigurationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"evaluation-offline-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Ai:ApiKey", "")]
    [InlineData("Ai:Model", "")]
    [InlineData("Ai:Endpoint", "not-a-uri-settings-secret")]
    [InlineData("Ai:Endpoint", "https://endpoint-secret@invalid.example/?key=settings-secret")]
    [InlineData("Ai:Temperature", "settings-secret")]
    [InlineData("Ai:Temperature", "3")]
    [InlineData("Ai:ReasoningEnabled", "settings-secret")]
    [InlineData("Ai:MaxOutputTokens", "0")]
    [InlineData("Ai:RequestTimeoutSeconds", "0")]
    [InlineData("Ai:RequestTimeoutSeconds", "settings-secret")]
    [InlineData("client-factory", "settings-secret")]
    public async Task Invalid_or_missing_ai_configuration_preserves_offline_dashboard(string setting, string value)
    {
        var providerBuilder = WebApplication.CreateSlimBuilder();
        providerBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        providerBuilder.Logging.ClearProviders();
        await using var provider = providerBuilder.Build();
        var calls = 0;
        provider.MapPost("/chat/completions", () => { calls++; return Results.StatusCode(500); });
        await provider.StartAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-ai-secret",
            ["Ai:Model"] = "test/offline",
            ["Ai:Endpoint"] = provider.Urls.Single(),
            [setting] = value
        });
        builder.Logging.ClearProviders();
        await using var app = EvaluationDashboard.Build(builder, 0, directory, services =>
        {
            if (setting != "client-factory") return;
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient>(_ => throw new InvalidOperationException(value));
        });
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("Origin", http.BaseAddress.GetLeftPart(UriPartial.Authority));
        var setup = await http.GetFromJsonAsync<JsonElement>("/api/setup");
        Assert.False(setup.GetProperty("configured").GetBoolean());
        AssertSafe(setup.GetRawText());
        http.DefaultRequestHeaders.Add("X-Evaluation-CSRF", setup.GetProperty("csrfToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/")).StatusCode);

        var id = EvaluationRunStore.NewId();
        var report = EvaluationReportsTests.CreateReport();
        report.Results.Add(new("reading", 1)
        {
            Plan = EvaluationFixtures.Plan(),
            Input = LearningPlanFixture.Resolve(EvaluationFixtures.Plan()),
            Materials = EvaluationReportsTests.Skipped(),
            Authoring = EvaluationReportsTests.Step(),
            Generation = EvaluationReportsTests.Step(role: "questions")
        });
        Directory.CreateDirectory(Path.Combine(directory, id));
        await EvaluationFiles.SaveAsync(report, Path.Combine(directory, id));
        Assert.Single(await http.GetFromJsonAsync<JsonElement[]>("/api/runs") ?? []);
        var review = new EvaluationReviewUpdate("reading", 1, new ManualReview { Hebrew = 2, Notes = "Offline review" });
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync($"/api/runs/{id}/review", review)).StatusCode);
        var saved = await http.GetFromJsonAsync<JsonElement>($"/api/runs/{id}");
        Assert.Equal(2, saved.GetProperty("report").GetProperty("results")[0].GetProperty("review").GetProperty("hebrew").GetInt32());
        var comparison = await http.GetFromJsonAsync<JsonElement>($"/api/compare?baseline={id}&candidate={id}");
        Assert.True(comparison.GetProperty("directlyComparable").GetBoolean());

        var response = await http.PostAsJsonAsync("/api/runs", new EvaluationRunRequest(["reading-grade3"], 1, false, 3, Confirmed: true));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AI configuration is missing or invalid. Fix it and restart the dashboard before starting a real evaluation.",
            problem.GetProperty("title").GetString());
        AssertSafe(problem.GetRawText());
        Assert.Equal("null", await http.GetStringAsync("/api/active"));
        Assert.Single(Directory.GetDirectories(directory));
        Assert.Equal(0, calls);
        await app.StopAsync();
    }

    [Fact]
    public async Task Dashboard_owns_and_disposes_its_ai_client_after_shutdown()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        var chat = new AiFixtures.ScriptedChat();
        await using var app = EvaluationDashboard.Build(builder, 0, directory,
            services => services.AddSingleton<IChatClient>(_ => chat));
        await app.StartAsync();
        Assert.True(app.Services.GetRequiredService<EvaluationCoordinator>().Configured);
        await app.StopAsync();
        Assert.Equal(0, chat.DisposeCalls);
        await app.DisposeAsync();
        Assert.Equal(1, chat.DisposeCalls);
        Assert.Empty(chat.Requests);
    }

    private static void AssertSafe(string text)
    {
        foreach (var sensitive in new[] { "isolated-ai-secret", "settings-secret", "endpoint-secret", "Exception", "StackTrace" })
            Assert.DoesNotContain(sensitive, text);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
