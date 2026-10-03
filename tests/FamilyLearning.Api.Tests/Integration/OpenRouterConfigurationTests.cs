using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class OpenRouterConfigurationTests
{
    [Theory]
    [InlineData(false, "low", 0.7f, 0.8f, "test/secondary:free", "json_schema")]
    [InlineData(null, null, null, null, null, null)]
    [InlineData(null, "", null, null, "", "text")]
    [InlineData(true, "", 1f, 0.95f, "", "json_object")]
    [InlineData(null, "", 1f, 0.95f, "", "json_schema", "test/paid", 8192, 20, 16384)]
    [InlineData(true, "", null, null, "test/paid", "json_schema")]
    [InlineData(null, "low", null, null, "", "json_schema", "test/model:free", null, null, null, true)]
    public async Task Generation_requests_preserve_settings_schema_guidance_and_unicode(bool? enabled, string? effort,
        float? temperature, float? topP, string? fallbackModel, string? responseFormat, string model = "test/model:free",
        int? reasoningMaxTokens = null, int? topK = null, int? maxOutputTokens = null, bool excludeProvider = false)
    {
        const string sourceText = "שָׁלוֹם, Maya! שלום־עולם";

        var definition = AiFixtures.PlanJson();
        var instructions = "יש ליצור משימה לפי \"sourceText\".\n\n" + sourceText;
        definition["guidance"] = instructions;
        var generated = JsonNode.Parse(GenerationHarness.Questions())!;
        var authoring = new JsonObject
        {
            ["result"] = new JsonObject { ["proposal"] = definition, ["clarification"] = null },
            ["assumptions"] = new JsonArray()
        };
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        JsonElement request = default;
        server.MapPost("/chat/completions", async (HttpRequest incoming) =>
        {
            using var body = await JsonDocument.ParseAsync(incoming.Body);
            request = body.RootElement.Clone();
            var isInstance = request.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("Create the complete question batch", StringComparison.Ordinal);
            return Results.Json(new
            {
                id = "local-test",
                model = "test:free",
                created = 0,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = (isInstance ? generated : authoring).ToJsonString() }, finish_reason = "stop" } }
            });
        });
        await server.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Endpoint"] = server.Urls.Single(),
            ["Ai:Model"] = model,
            ["Ai:ResponseFormat"] = responseFormat,
            ["Ai:FallbackModel"] = fallbackModel,
            ["Ai:ReasoningEnabled"] = enabled?.ToString(),
            ["Ai:ReasoningEffort"] = effort,
            ["Ai:ReasoningMaxTokens"] = reasoningMaxTokens?.ToString(CultureInfo.InvariantCulture),
            ["Ai:Temperature"] = temperature?.ToString(CultureInfo.InvariantCulture),
            ["Ai:TopP"] = topP?.ToString(CultureInfo.InvariantCulture),
            ["Ai:TopK"] = topK?.ToString(CultureInfo.InvariantCulture),
            ["Ai:MaxOutputTokens"] = (maxOutputTokens ?? 8192).ToString(CultureInfo.InvariantCulture)
        }).Build();
        if (excludeProvider) configuration["Ai:IgnoredProviders:0"] = "test-provider";
        var services = new ServiceCollection().AddLogging();
        services.AddTaskAi(configuration, new HostingEnvironment { EnvironmentName = "Development" });
        using var provider = services.BuildServiceProvider();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await provider.GetRequiredService<AiGenerationService>().AuthorAsync(new("A learning idea"), deadline.Token);

        Assert.Equal(model, request.GetProperty("model").GetString());
        if (string.IsNullOrWhiteSpace(fallbackModel)) Assert.False(request.TryGetProperty("models", out _));
        else Assert.Equal(fallbackModel, Assert.Single(request.GetProperty("models").EnumerateArray()).GetString());
        Assert.Equal("test:free", result.Metadata.Model);
        Assert.Equal(instructions, result.Value.Proposal!.Guidance);
        if (enabled is null && string.IsNullOrEmpty(effort) && reasoningMaxTokens is null)
        {
            Assert.False(request.TryGetProperty("reasoning", out _));
        }
        else
        {
            var reasoning = request.GetProperty("reasoning");
            Assert.True(reasoning.GetProperty("exclude").GetBoolean());
            if (enabled != false && reasoningMaxTokens.HasValue)
            {
                Assert.Equal(reasoningMaxTokens, reasoning.GetProperty("max_tokens").GetInt32());
                Assert.False(reasoning.TryGetProperty("effort", out _));
                Assert.False(reasoning.TryGetProperty("enabled", out _));
            }
            else if (enabled != false && !string.IsNullOrEmpty(effort))
            {
                Assert.Equal(effort, reasoning.GetProperty("effort").GetString());
                Assert.False(reasoning.TryGetProperty("enabled", out _));
            }
            else
            {
                Assert.Equal(enabled, reasoning.GetProperty("enabled").GetBoolean());
                Assert.False(reasoning.TryGetProperty("effort", out _));
            }
            if (enabled == false || !reasoningMaxTokens.HasValue)
                Assert.False(reasoning.TryGetProperty("max_tokens", out _));
        }
        Assert.Equal(temperature, request.TryGetProperty("temperature", out var value) ? value.GetSingle() : null);
        Assert.Equal(topP, request.TryGetProperty("top_p", out value) ? value.GetSingle() : null);
        Assert.Equal(topK, request.TryGetProperty("top_k", out value) ? value.GetInt32() : null);
        Assert.Equal(maxOutputTokens ?? 8192, request.GetProperty("max_completion_tokens").GetInt32());
        Assert.True(request.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        if (excludeProvider)
            Assert.Equal("test-provider", Assert.Single(request.GetProperty("provider").GetProperty("ignore").EnumerateArray()).GetString());
        else Assert.False(request.GetProperty("provider").TryGetProperty("ignore", out _));
        AssertResponseSchema(request, responseFormat ?? "json_object", "proposal");

        var resolved = TaskEngine.LearningPlanFixture.Resolve(TaskEngine.LearningPlanFixture.Supplied() with
        { Defaults = TaskEngine.LearningPlanFixture.Numeric(1).Defaults });
        var document = Api.TaskEngine.TaskAssembly.CreateDocument(resolved);
        var questions = await provider.GetRequiredService<AiGenerationService>().GenerateQuestionsAsync(new(resolved, document.Materials), deadline.Token);
        Assert.Equal("2", Assert.Single(questions.Value.Questions).Answer!.Value);
        AssertResponseSchema(request, responseFormat ?? "json_object", "questions", questionCount: 1);
        Assert.Equal(maxOutputTokens ?? 8192, request.GetProperty("max_completion_tokens").GetInt32());
        using var input = JsonDocument.Parse(request.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(TaskEngine.LearningPlanFixture.Source, input.RootElement.GetProperty("materials")[0].GetProperty("body").GetString());

        definition["unexpected"] = true;
        await Assert.ThrowsAsync<AiGenerationException>(() => provider.GetRequiredService<AiGenerationService>()
            .AuthorAsync(new("A learning idea"), deadline.Token));
    }

    private static void AssertResponseSchema(JsonElement request, string responseFormat, string description, int? questionCount = null)
    {
        var prompt = request.GetProperty("messages")[0].GetProperty("content").GetString()!;
        // Strict mode carries the schema once, natively; other modes can only carry it in the prompt.
        Assert.Equal(responseFormat != "json_schema", prompt.Contains("\nOutput JSON schema:\n"));
        using var schema = JsonDocument.Parse(responseFormat == "json_schema"
            ? request.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetRawText()
            : prompt.Split("\nOutput JSON schema:\n")[1]);
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.False(schema.RootElement.TryGetProperty("anyOf", out _));
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(description, schema.RootElement.GetRawText());
        if (questionCount.HasValue)
        {
            var questions = schema.RootElement.GetProperty("properties").GetProperty("questions");
            Assert.Equal(questionCount, questions.GetProperty("minItems").GetInt32());
            Assert.Equal(questionCount, questions.GetProperty("maxItems").GetInt32());
        }
        if (responseFormat == "text")
        {
            Assert.False(request.TryGetProperty("response_format", out _));
            return;
        }
        var format = request.GetProperty("response_format");
        Assert.Equal(responseFormat, format.GetProperty("type").GetString());
        if (responseFormat == "json_schema") Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        else Assert.False(format.TryGetProperty("json_schema", out _));
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":429,\"message\":\"provider secret\"}}", 429)]
    [InlineData("{\"error\":{\"code\":503,\"message\":\"provider secret\"}}", 502)]
    [InlineData("{\"choices\":[]}", 502)]
    [InlineData("""{"created":"provider secret","choices":[{"message":{"content":"{}"}}]}""", 502)]
    [InlineData("""{"choices":[{"message":{"content":[null]}}]}""", 502)]
    [InlineData("""{"choices":[{"message":{"tool_calls":[{"type":"function","function":{"name":"f","arguments":"{}"}}]}}]}""", 502)]
    [InlineData("not JSON", 502)]
    public async Task Failed_completions_in_HTTP_200_are_safe_and_do_not_retry(string body, int status)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        var requests = 0;
        server.MapPost("/chat/completions", () =>
        {
            requests++;
            return Results.Text(body, "application/json");
        });
        await server.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Model"] = "test/model:free",
            ["Ai:Endpoint"] = server.Urls.Single()
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddTaskAi(configuration, new HostingEnvironment { EnvironmentName = "Development" });
        using var provider = services.BuildServiceProvider();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var service = provider.GetRequiredService<AiGenerationService>();

        // More calls than available slots also verifies failures release capacity.
        for (var i = 0; i < 3; i++)
        {
            var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new("A learning idea"), deadline.Token));
            Assert.Equal(status, error.StatusCode);
            Assert.DoesNotContain("provider secret", error.Message);
        }
        Assert.Equal(3, requests);
    }

    [Theory]
    [InlineData("Ai:Temperature", "3")]
    [InlineData("Ai:TopP", "0")]
    [InlineData("Ai:TopK", "-1")]
    [InlineData("Ai:ReasoningMaxTokens", "8192")]
    [InlineData("Ai:ReasoningMaxTokens", "2048", "low")]
    [InlineData("Ai:ReasoningEffort", "unlimited")]
    [InlineData("Ai:ReasoningEnabled", "maybe")]
    [InlineData("Ai:ResponseFormat", "maybe")]
    [InlineData("Ai:Model", "")]
    [InlineData("Ai:Model", "paid/model,test:free")]
    [InlineData("Ai:IgnoredProviders:0", "https://provider")]
    public void Invalid_generation_settings_are_rejected(string setting, string? value, string? effort = null,
        string maxOutputTokens = "8192")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Model"] = "test/model:free",
            ["Ai:ReasoningEffort"] = effort,
            ["Ai:MaxOutputTokens"] = maxOutputTokens,
            [setting] = value
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddTaskAi(configuration, new HostingEnvironment()));
        Assert.Contains(setting.StartsWith("Ai:IgnoredProviders:", StringComparison.Ordinal) ? "Ai:IgnoredProviders" : setting, error.Message);
    }

    [Fact]
    public void Provider_exclusions_are_recorded_in_the_nonsecret_profile()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Model"] = "test/model",
            ["Ai:IgnoredProviders:0"] = "test-provider"
        }).Build();
        var profile = AiProfile.Capture(configuration, new());
        Assert.Equal("test-provider", profile["IgnoredProviders"]);
        Assert.DoesNotContain("isolated-test-key", JsonSerializer.Serialize(profile));
    }

    [Theory]
    [InlineData(17, 1)]
    [InlineData(1, 65)]
    public void Provider_exclusions_reject_excessive_entries_or_slug_length(int count, int length)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Model"] = "test/model"
        }).Build();
        for (var i = 0; i < count; i++) configuration[$"Ai:IgnoredProviders:{i}"] = new string('a', length);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTaskAi(configuration, new HostingEnvironment()));
    }
}
