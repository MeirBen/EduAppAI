using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using FamilyLearning.Api.Tests.TaskEngine;
using FamilyLearning.Evaluation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class EvaluationCommandTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"learning-evaluation-cli-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false, 4, 0, 0, false)]
    [InlineData(true, 1, 2, 0, false)]
    [InlineData(true, 3, 0, 3, false)]
    [InlineData(true, 3, 0, 3, true)]
    [InlineData(true, 2, 2, 0, false, "malformed-endpoint")]
    [InlineData(true, 4, 0, 4, false, null, true, "0")]
    [InlineData(true, 4, 0, 4, true, null, true, "Thu, 01 Jan 1970 00:00:00 GMT")]
    [InlineData(true, 5, 1, 1, false, null, true, "301")]
    public async Task Cli_and_dashboard_use_the_real_adapter_without_database_access(
        bool live, int budget, int expectedExitCode, int expectedCalls, bool dashboard, string? endpoint = null,
        bool rateLimitFirst = false, string retryAfter = "0")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        var calls = 0;
        server.MapPost("/chat/completions", async (HttpRequest incoming, HttpResponse outgoing) =>
        {
            using var request = await JsonDocument.ParseAsync(incoming.Body);
            Assert.Equal("test/evaluation", request.RootElement.GetProperty("model").GetString());
            Assert.Equal(1, request.RootElement.GetProperty("temperature").GetInt32());
            Assert.True(request.RootElement.GetProperty("reasoning").GetProperty("exclude").GetBoolean());
            Assert.Equal("json_schema", request.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            calls++;
            if (rateLimitFirst && calls == 1)
            {
                outgoing.Headers.RetryAfter = retryAfter;
                return Results.Json(new { error = new { message = "provider secret", code = 429 } }, statusCode: 429);
            }
            var schema = request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString()!;
            JsonNode output;
            if (schema.Contains("author", StringComparison.Ordinal))
            {
                var plan = LearningPlanFixture.Reading() with
                {
                    Defaults = new("דינוזאורים", "כיתה ג", "easy", 4),
                    Materials = [LearningPlanFixture.Reading().Materials[0] with
                    {
                        Id = null, Controls = [],
                        Length = new("range", Lower: 100, Upper: 150)
                    }],
                    Questions = new(["single-choice"], false, null, new(4, false), null, "", [])
                };
                output = JsonNode.Parse(StructuredEvaluationTests.Proposal(plan))!;
            }
            else if (schema.Contains("materials", StringComparison.Ordinal))
            {
                var messages = request.RootElement.GetProperty("messages");
                using var input = JsonDocument.Parse(messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!);
                var id = input.RootElement.GetProperty("materials")[0].GetProperty("id").GetString();
                output = JsonSerializer.SerializeToNode(new { materials = new[] { new { id, title = "קריאה", body = string.Join(' ', Enumerable.Repeat("מילה", 100)) } } })!;
            }
            else
            {
                output = EvaluationFixtures.Content(4);
                foreach (var question in output["questions"]!.AsArray())
                    question!["interaction"] = new JsonObject
                    {
                        ["type"] = "single-choice",
                        ["options"] = new JsonArray("דינוזאורים", "עצים", "ציפורים", "פרחים")
                    };
            }
            return Results.Json(new
            {
                id = "evaluation-local",
                model = "test/actual",
                created = 0,
                choices = new[] { new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = output.ToJsonString(), reasoning = "private-reasoning" } } },
                usage = new { prompt_tokens = 100, completion_tokens = 40, total_tokens = 140, completion_tokens_details = new { reasoning_tokens = 10 }, cost = 0.001m }
            });
        });
        await server.StartAsync();
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory };
        start.ArgumentList.Add(typeof(EvaluationCommand).Assembly.Location);
        if (live) start.ArgumentList.Add("--live");
        start.ArgumentList.Add("--call-delay-seconds");
        start.ArgumentList.Add("0");
        start.ArgumentList.Add("--max-calls");
        start.ArgumentList.Add(budget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(directory);
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["Ai__ApiKey"] = "isolated-test-secret";
        start.Environment["Ai__Endpoint"] = endpoint ?? server.Urls.Single();
        start.Environment["Ai__Model"] = "test/evaluation";
        start.Environment["Ai__FallbackModel"] = "";
        start.Environment["Ai__ResponseFormat"] = "json_schema";
        start.Environment["Ai__Temperature"] = "1";
        start.Environment["Ai__TopP"] = "0.95";
        start.Environment["Ai__TopK"] = "20";
        start.Environment["Ai__ReasoningEnabled"] = "true";
        start.Environment["Ai__ReasoningMaxTokens"] = "2048";
        start.Environment["Ai__ReasoningEffort"] = "";
        start.Environment["Ai__MaxOutputTokens"] = "8192";
        start.Environment["Storage__Directory"] = Path.Combine(directory, "must-not-exist");
        if (dashboard)
        {
            var uiBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            uiBuilder.Configuration.Sources.Clear();
            uiBuilder.Configuration.AddInMemoryCollection(start.Environment.Where(pair => pair.Key.StartsWith("Ai__", StringComparison.Ordinal))
                .Select(pair => new KeyValuePair<string, string?>(pair.Key.Replace("__", ":"), pair.Value)));
            uiBuilder.Logging.ClearProviders();
            await using var ui = EvaluationDashboard.Build(uiBuilder, 0, directory);
            await ui.StartAsync();
            using var http = new HttpClient { BaseAddress = new(ui.Urls.Single()) };
            http.DefaultRequestHeaders.Add("Origin", http.BaseAddress.GetLeftPart(UriPartial.Authority));
            var setup = await http.GetFromJsonAsync<JsonElement>("/api/setup");
            http.DefaultRequestHeaders.Add("X-Evaluation-CSRF", setup.GetProperty("csrfToken").GetString());
            Assert.Equal(0, calls);
            var response = await http.PostAsJsonAsync("/api/runs", new EvaluationRunRequest(["reading-grade3"], 1, false, budget, Confirmed: true, CallDelaySeconds: 0));
            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            await ui.Services.GetRequiredService<EvaluationCoordinator>().WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("completed", ui.Services.GetRequiredService<EvaluationCoordinator>().Active!.Status);
            await ui.StopAsync();
        }
        else
        {
            var process = await TestProcess.RunAsync(start);
            Assert.True(process.ExitCode == expectedExitCode, process.Output + process.Error);
            Assert.DoesNotContain("isolated-test-secret", process.Output + process.Error);
            Assert.DoesNotContain("Exception", process.Error);
        }
        Assert.Equal(expectedCalls, calls);
        Assert.False(Directory.Exists(start.Environment["Storage__Directory"]));
        var reports = Directory.GetFiles(directory, "run.json", SearchOption.AllDirectories);
        if (expectedCalls == 0) { Assert.Empty(reports); return; }
        var json = await File.ReadAllTextAsync(Assert.Single(reports));
        Assert.DoesNotContain("isolated-test-secret", json);
        Assert.DoesNotContain("private-reasoning", json);
        Assert.DoesNotContain("provider secret", json);
        var summaryJson = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(reports[0])!, "summary.json"));
        Assert.DoesNotContain("isolated-test-secret", summaryJson);
        Assert.DoesNotContain("private-reasoning", summaryJson);
        using var report = JsonDocument.Parse(json);
        Assert.Equal(expectedCalls, report.RootElement.GetProperty("attemptedCalls").GetInt32());
        if (expectedCalls == 1)
        {
            Assert.Equal("rate-limited", report.RootElement.GetProperty("status").GetString());
            Assert.Empty(report.RootElement.GetProperty("retries").EnumerateArray());
            Assert.Equal(301, report.RootElement.GetProperty("results")[0].GetProperty("authoring").GetProperty("retryAfterSeconds").GetDouble());
            return;
        }
        if (rateLimitFirst)
        {
            var retry = Assert.Single(report.RootElement.GetProperty("retries").EnumerateArray());
            Assert.Equal(0, retry.GetProperty("call").GetProperty("retryAfterSeconds").GetDouble());
            Assert.Equal(0, retry.GetProperty("delaySeconds").GetDouble());
        }
        Assert.Equal(0.003m, report.RootElement.GetProperty("reportedCostCredits").GetDecimal());
        Assert.Equal(3, report.RootElement.GetProperty("callsWithReportedCost").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("automaticPasses").GetInt32());
        var authoring = report.RootElement.GetProperty("results")[0].GetProperty("authoring");
        Assert.Equal(10, authoring.GetProperty("reasoningTokens").GetInt32());
        Assert.Equal("test/actual", authoring.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    public async Task Compare_is_offline_and_reports_invalid_inputs_safely(bool extraLiveFlag, bool invalidReport, int expectedExit)
    {
        Directory.CreateDirectory(directory);
        var report = EvaluationReportsTests.CreateReport();
        report.Results.Add(new("reading", 1)
        {
            Plan = EvaluationFixtures.Plan(),
            Input = LearningPlanFixture.Resolve(EvaluationFixtures.Plan()),
            Materials = EvaluationReportsTests.Skipped(),
            Authoring = EvaluationReportsTests.Step(),
            Generation = EvaluationReportsTests.Step(role: "questions")
        });
        await EvaluationFiles.SaveAsync(report, directory);
        var path = Path.Combine(directory, "run.json");
        if (invalidReport)
        {
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            json["formatVersion"] = 1;
            await File.WriteAllTextAsync(path, json.ToJsonString());
        }
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory };
        foreach (var argument in new[] { typeof(EvaluationCommand).Assembly.Location, "--compare", path, path }) start.ArgumentList.Add(argument);
        if (extraLiveFlag) start.ArgumentList.Add("--live");
        // Resolving AI would fail this configuration. Comparison must not even compose the provider.
        start.Environment["Ai__ApiKey"] = "isolated-secret";
        start.Environment["Ai__Model"] = "";
        start.Environment["Ai__Endpoint"] = "https://invalid.example";
        var process = await TestProcess.RunAsync(start);
        Assert.True(process.ExitCode == expectedExit, process.Output + process.Error);
        Assert.DoesNotContain("isolated-secret", process.Output + process.Error);
        Assert.DoesNotContain("Exception", process.Error);
        if (expectedExit == 0)
        {
            using var comparison = JsonDocument.Parse(process.Output);
            Assert.True(comparison.RootElement.GetProperty("directlyComparable").GetBoolean());
            Assert.Equal(0, comparison.RootElement.GetProperty("deltas").GetProperty("scenarioAutomaticPasses").GetInt32());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
