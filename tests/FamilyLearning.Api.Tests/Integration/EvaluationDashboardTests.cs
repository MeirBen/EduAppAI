using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Api.Tests.TaskEngine;
using FamilyLearning.Evaluation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class EvaluationDashboardTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"evaluation-ui-{Guid.NewGuid():N}");
    private AiFixtures.ScriptedChat chat = null!;
    private Func<CancellationToken, Task>? beforeResponse;
    private WebApplication app = null!;
    private HttpClient http = null!;
    private string token = "";

    public async Task InitializeAsync()
    {
        chat = new(AiFixtures.Definition().ToJsonString(), AiFixtures.Content().ToJsonString()) { BeforeResponse = ct => beforeResponse?.Invoke(ct) ?? Task.CompletedTask };
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-dashboard-secret",
            ["Ai:Model"] = "test/dashboard",
            ["Ai:ReasoningEnabled"] = "true",
            ["Kestrel:Endpoints:Unwanted:Url"] = "http://0.0.0.0:0",
            ["urls"] = "http://0.0.0.0:0"
        });
        builder.Logging.ClearProviders();
        app = EvaluationDashboard.Build(builder, 0, directory, services =>
        {
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient>(chat);
        });
        await app.StartAsync();
        http = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new(app.Urls.Single()) };
        http.DefaultRequestHeaders.Add("Origin", http.BaseAddress.GetLeftPart(UriPartial.Authority));
        var setup = await http.GetFromJsonAsync<JsonElement>("/api/setup");
        token = setup.GetProperty("csrfToken").GetString()!;
        http.DefaultRequestHeaders.Add("X-Evaluation-CSRF", token);
    }

    [Fact]
    public async Task Startup_and_read_only_routes_are_local_safe_and_make_no_provider_calls()
    {
        Assert.Equal("127.0.0.1", http.BaseAddress!.Host);
        var setup = await http.GetStringAsync("/api/setup");
        Assert.DoesNotContain("isolated-dashboard-secret", setup);
        Assert.Contains("test/dashboard", setup);
        Assert.Equal("[]", await http.GetStringAsync("/api/runs"));
        Assert.Equal("null", await http.GetStringAsync("/api/active"));
        var page = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(page.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Empty(chat.Requests);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("origin")]
    [InlineData("csrf")]
    public async Task Foreign_or_unprotected_requests_cannot_start_paid_work(string missing)
    {
        if (missing == "host") http.DefaultRequestHeaders.Host = "evil.example";
        if (missing == "origin")
        {
            http.DefaultRequestHeaders.Remove("Origin");
            http.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        }
        if (missing == "csrf") http.DefaultRequestHeaders.Remove("X-Evaluation-CSRF");
        var response = await http.PostAsJsonAsync("/api/runs", Request());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task Start_recomputes_budget_and_requires_explicit_confirmation()
    {
        foreach (var request in new[] { Request() with { MaxCalls = 1 }, Request() with { Confirmed = false }, Request() with { CaseIds = ["missing"] },
            Request() with { Prototype = true, CaseIds = ["all"], MaxCalls = 21 },
            Request() with { CallDelaySeconds = -1 }, Request() with { CallDelaySeconds = 61 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/runs", request)).StatusCode);
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task Only_one_run_is_owned_and_cancellation_retains_its_partial_report()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        beforeResponse = async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var start = await http.PostAsJsonAsync("/api/runs", Request());
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/runs", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PutAsJsonAsync($"/api/runs/{id}/review",
            new { caseId = "reading-grade3", repetition = 1, review = new ManualReview() })).StatusCode);
        var cancel = await http.PostAsJsonAsync($"/api/runs/{id}/cancel", new { });
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal("cancelling", (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await app.Services.GetRequiredService<EvaluationCoordinator>().WaitAsync();
        var report = await new EvaluationRunStore(directory).ReadAsync(id);
        Assert.Equal("cancelled", report.Status);
        Assert.Equal("cancelled", report.Results[0].Authoring!.Failure);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task History_review_and_comparison_only_use_saved_evidence()
    {
        var id = EvaluationRunStore.NewId();
        var report = EvaluationReportsTests.CreateReport();
        report.Results.Add(new("reading", 1)
        {
            Authoring = EvaluationReportsTests.Step(),
            Generation = EvaluationReportsTests.Step()
        });
        report.Results[0].Generation!.Output = "<script>alert('model')</script>";
        Directory.CreateDirectory(Path.Combine(directory, id));
        await EvaluationFiles.SaveAsync(report, Path.Combine(directory, id));
        var before = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, id, "run.json")))!;
        var review = new ManualReview { Hebrew = 2, Notes = "בדיקה <script> אינה HTML" };
        var response = await http.PutAsJsonAsync($"/api/runs/{id}/review", new { caseId = "reading", repetition = 1, review });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<EvaluationSummary>();
        Assert.Equal(new HumanScoreSummary(1, 0, 2), summary!.HumanReview["hebrew"]);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, id, "run.json")))!;
        before["results"]![0]!["review"] = after["results"]![0]!["review"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(before, after));
        var saved = await http.GetFromJsonAsync<JsonElement>($"/api/runs/{id}");
        Assert.Equal(report.Results[0].Generation!.Output, saved.GetProperty("report").GetProperty("results")[0].GetProperty("generation").GetProperty("output").GetString());
        var comparison = await http.GetFromJsonAsync<JsonElement>($"/api/compare?baseline={id}&candidate={id}");
        Assert.True(comparison.GetProperty("directlyComparable").GetBoolean());
        Assert.Single(await http.GetFromJsonAsync<JsonElement[]>("/api/runs") ?? []);
        foreach (var invalid in new[] { new ManualReview { Hebrew = 3 }, new ManualReview { Notes = new string('a', 4001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync($"/api/runs/{id}/review",
                new { caseId = "reading", repetition = 1, review = invalid })).StatusCode);
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task Fake_run_completes_with_progress_and_history_makes_no_further_calls()
    {
        var response = await http.PostAsJsonAsync("/api/runs", Request());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var coordinator = app.Services.GetRequiredService<EvaluationCoordinator>();
        await coordinator.WaitAsync();
        Assert.False(coordinator.Active!.Running);
        Assert.Equal("completed", coordinator.Active.Status);
        Assert.Equal(2, coordinator.Active.Progress!.CompletedCalls);
        Assert.Equal("test-free-model", coordinator.Active.Progress.Model);
        Assert.Null(coordinator.Active.Progress.ReportedCostCredits);
        Assert.Equal(2, coordinator.Active.Progress.MissingCostCalls);
        Assert.Equal(2, chat.Requests.Count);
        await http.GetStringAsync($"/api/runs/{id}");
        await http.GetStringAsync($"/api/compare?baseline={id}&candidate={id}");
        Assert.Equal(2, chat.Requests.Count);
    }

    [Fact]
    public async Task Host_shutdown_cancels_and_checkpoints_owned_work()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        beforeResponse = async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var response = await http.PostAsJsonAsync("/api/runs", Request());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var store = new EvaluationRunStore(directory);
        Assert.Equal("cancelled", (await store.ReadAsync(id)).Status);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task Unhandled_provider_failure_is_observed_without_returning_exception_text()
    {
        beforeResponse = _ => throw new InvalidOperationException("secret-provider-exception");
        var response = await http.PostAsJsonAsync("/api/runs", Request());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await app.Services.GetRequiredService<EvaluationCoordinator>().WaitAsync();
        var active = await http.GetStringAsync("/api/active");
        Assert.Contains("failed", active);
        Assert.DoesNotContain("secret-provider-exception", active);
        Assert.Single(chat.Requests);
    }

    [Fact]
    public async Task Unknown_mutation_fields_and_missing_origin_are_rejected()
    {
        var request = JsonSerializer.SerializeToNode(Request(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        request["output"] = "/tmp/escape";
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/runs", request)).StatusCode);
        http.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/runs", Request())).StatusCode);
        Assert.Empty(chat.Requests);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/tmp/outside")]
    [InlineData("..\\outside")]
    public async Task Store_rejects_traversal(string id) =>
        await Assert.ThrowsAsync<ArgumentException>(() => new EvaluationRunStore(directory).ReadAsync(id));

    private static EvaluationRunRequest Request() => new(["reading-grade3"], 1, false, 2, Confirmed: true, CallDelaySeconds: 0);

    public async Task DisposeAsync()
    {
        http.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
