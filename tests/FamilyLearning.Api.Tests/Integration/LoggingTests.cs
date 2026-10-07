using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Logging;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static FamilyLearning.Api.Tests.Integration.ActivityDraftTests;
using static FamilyLearning.Api.Tests.Integration.GenerationHarness;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class LoggingTests : IDisposable
{
    private readonly string directory = TemporaryDirectory();

    [Fact]
    public async Task Invalid_configuration_is_logged_before_the_host_exists()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "appsettings.json"), "{invalid");
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(directory);
        var result = await TestProcess.RunAsync(start);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Host configuration failed", result.Output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relative_file_paths_use_the_solution_root_or_deployed_content_root(bool inRepository)
    {
        var contentRoot = System.IO.Path.Combine(directory, "backend", "server");
        Directory.CreateDirectory(contentRoot);
        if (inRepository) await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "FamilyLearning.sln"), "");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot });
        builder.Configuration.AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        builder.AddApplicationLogging();
        await using (var host = builder.Build()) host.Logger.LogInformation("Logging location verified");

        var entries = ReadEntries(inRepository ? directory : contentRoot);
        Assert.Contains(entries, entry => entry.GetProperty("@m").GetString() == "Logging location verified");
        if (inRepository) Assert.False(Directory.Exists(System.IO.Path.Combine(contentRoot, "logs")));
    }

    [Fact]
    public async Task Hosts_sharing_a_log_file_keep_every_entry_whole()
    {
        Directory.CreateDirectory(directory);
        WebApplication Host()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = directory });
            builder.Configuration.AddJsonFile(System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            builder.AddApplicationLogging();
            return builder.Build();
        }
        await using (var server = Host())
        await using (var command = Host())
            for (var i = 0; i < 10; i++)
            {
                server.Logger.LogInformation("Server entry {Index}", i);
                command.Logger.LogInformation("Command {Index}", i);
            }

        var messages = ReadEntries(directory).Select(entry => entry.GetProperty("@m").GetString()).ToArray();
        Assert.Equal(10, messages.Count(message => message!.StartsWith("Server entry")));
        Assert.Equal(10, messages.Count(message => message!.StartsWith("Command")));
    }

    [Fact]
    public async Task Requests_persist_once_with_levels_and_correlation_without_credentials_or_query_strings()
    {
        string traceId;
        await using (var app = new ApiFactory(storageDirectory: directory))
        {
            using var anonymous = app.CreateClient();
            using var denied = await anonymous.GetAsync("/api/limits?private-query=secret-value");
            traceId = (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString()!;
            using var parent = await app.ParentAsync();
            Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync("/api/limits")).StatusCode);
        }

        var entries = ReadEntries(directory);
        var deniedLog = Assert.Single(entries, entry => HasStatus(entry, 401));
        Assert.Equal("Warning", deniedLog.GetProperty("@l").GetString());
        Assert.Equal(traceId, deniedLog.GetProperty("RequestId").GetString());
        var success = Assert.Single(entries, entry => HasPath(entry, "/api/auth/login") && HasStatus(entry, 204));
        Assert.False(success.TryGetProperty("@l", out _)); // Compact JSON omits the default Information level.
        Assert.True(success.TryGetProperty("Elapsed", out _));
        Assert.True(success.TryGetProperty("@tr", out _));
        Assert.Contains("responded 204 in ", success.GetProperty("@m").GetString());
        Assert.DoesNotContain("{Request", success.GetProperty("@m").GetString());
        Assert.DoesNotContain(entries, entry => HasPath(entry, "/health"));
        var text = string.Join('\n', entries);
        Assert.DoesNotContain("secret-value", text);
        Assert.DoesNotContain("Testing!Passphrase123", text);
        Assert.DoesNotContain("@example.test", text);
        Assert.DoesNotContain("X-XSRF-TOKEN", text);
    }

    [Fact]
    public async Task Unhandled_request_failure_has_one_error_with_exception_and_matching_problem_trace()
    {
        await using var app = new GenerationHarness { StorageDirectory = directory };
        using var parent = await app.ParentAsync();
        app.Chat.BeforeResponse = _ => throw new InvalidOperationException("Synthetic logging failure");
        using var response = await parent.PostAsJsonAsync("/api/ai/template-drafts", new TemplateAuthoringInput("private-parent-prompt"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var traceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString();
        await app.DisposeAsync();

        var entries = ReadEntries(directory);
        var error = Assert.Single(entries, entry => entry.TryGetProperty("@l", out var level) && level.GetString() == "Error");
        Assert.Equal(traceId, error.GetProperty("RequestId").GetString());
        Assert.Equal(500, error.GetProperty("StatusCode").GetInt32());
        Assert.Contains("Synthetic logging failure", error.GetProperty("@x").GetString());
        Assert.DoesNotContain("private-parent-prompt", string.Join('\n', entries));
    }

    [Fact]
    public async Task Generation_events_correlate_provider_and_committed_outcome_without_polling_or_content()
    {
        await using var app = new GenerationHarness(Questions()) { StorageDirectory = directory };
        using var parent = await app.ParentAsync();
        var draft = await Create(parent, Numeric(1));
        var request = new { operationKey = Guid.NewGuid(), expectedRevision = 1, kind = "GenerateQuestions" };
        using var start = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        var operation = (await start.Content.ReadFromJsonAsync<JsonNode>())!;
        using var replay = await parent.PostAsJsonAsync(Path(draft) + "/operations", request);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.True(await app.Worker.RunNextAsync(default));
        for (var i = 0; i < 3; i++) await parent.GetAsync(OperationPath(operation));
        await app.DisposeAsync();

        var entries = ReadEntries(directory);
        var provider = Assert.Single(entries, entry => entry.TryGetProperty("PromptVersion", out _));
        Assert.Equal(operation["id"]!.GetValue<Guid>(), provider.GetProperty("OperationId").GetGuid());
        Assert.Equal(draft["id"]!.GetValue<Guid>(), provider.GetProperty("DraftId").GetGuid());
        Assert.Equal("questions", provider.GetProperty("Stage").GetString());
        var starts = entries.Where(entry => HasStatus(entry, 202)).ToArray();
        Assert.Equal(2, starts.Length);
        Assert.All(starts, entry => Assert.Equal(operation["id"]!.GetValue<Guid>(), entry.GetProperty("OperationId").GetGuid()));
        Assert.Single(entries, entry => entry.TryGetProperty("Status", out var status) && status.GetString() == "completed");
        Assert.DoesNotContain(entries, entry => HasPath(entry, "/api/activity-drafts/{id:guid}/operations/{operationId:guid}") && HasStatus(entry, 200));
        Assert.DoesNotContain("כמה הם", string.Join('\n', entries));
    }

    private static string TemporaryDirectory() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "family-learning-logging", Guid.NewGuid().ToString());
    private static JsonElement[] ReadEntries(string directory) => Directory.GetFiles(System.IO.Path.Combine(directory, "logs"), "*.jsonl")
        .SelectMany(File.ReadAllLines).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
    private static bool HasStatus(JsonElement entry, int expected) => entry.TryGetProperty("StatusCode", out var status) && status.GetInt32() == expected;
    private static bool HasPath(JsonElement entry, string expected) => entry.TryGetProperty("RequestPath", out var path) && path.GetString() == expected;
    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
