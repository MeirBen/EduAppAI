using System.Globalization;
using System.Text.Json;
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
    [InlineData(true, "medium", 1f, 0.95f, "test/secondary:free", "json_schema")]
    [InlineData(null, "low", null, null, "", "json_schema")]
    [InlineData(null, null, null, null, null, null)]
    [InlineData(null, "", null, null, "", "text")]
    [InlineData(true, "", 1f, 0.95f, "", "json_object")]
    [InlineData(null, "", 1f, 0.95f, "", "json_schema", "test/paid", 8192, 20, 16384)]
    [InlineData(false, "", null, null, "", "json_schema", "test/paid", 2048)]
    [InlineData(true, "", null, null, "test/paid", "json_schema")]
    [InlineData(null, "", null, null, "", "json_object", "test/other", null, 0)]
    public async Task Generation_requests_preserve_settings_schema_guidance_and_unicode(bool? enabled, string? effort,
        float? temperature, float? topP, string? fallbackModel, string? responseFormat, string model = "test/model:free",
        int? reasoningMaxTokens = null, int? topK = null, int? maxOutputTokens = null)
    {
        const string sourceText = "שָׁלוֹם, Maya! שלום־עולם";
        const string passage = sourceText + "\n\nA second paragraph.";
        var definition = AiFixtures.Definition();
        var instructions = "יש ליצור משימה לפי \"theme\".\n\n" + sourceText;
        definition["generation"]!["instructions"] = instructions;
        var generated = AiFixtures.Content();
        generated["contentBlocks"]![0]!["text"] = passage;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        JsonElement request = default;
        server.MapPost("/chat/completions", async (HttpRequest incoming) =>
        {
            using var body = await JsonDocument.ParseAsync(incoming.Body);
            request = body.RootElement.Clone();
            var isInstance = request.GetProperty("messages")[1].GetProperty("content").GetString()!.StartsWith('{');
            return Results.Json(new
            {
                id = "local-test",
                model = "test:free",
                created = 0,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = (isInstance ? generated : definition).ToJsonString() }, finish_reason = "stop" } }
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
        var services = new ServiceCollection().AddLogging();
        services.AddTaskAi(configuration, new HostingEnvironment { EnvironmentName = "Development" });
        using var provider = services.BuildServiceProvider();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await provider.GetRequiredService<AiGenerationService>().AuthorAsync("A learning idea", deadline.Token);

        Assert.Equal(model, request.GetProperty("model").GetString());
        if (string.IsNullOrWhiteSpace(fallbackModel)) Assert.False(request.TryGetProperty("models", out _));
        else Assert.Equal(fallbackModel, Assert.Single(request.GetProperty("models").EnumerateArray()).GetString());
        Assert.Equal("test:free", result.Metadata.Model);
        Assert.Equal(instructions, result.Value.Generation.Instructions);
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
        AssertResponseSchema(request, responseFormat ?? "json_object", "task generator");

        var instance = await provider.GetRequiredService<AiGenerationService>().GenerateAsync(result.Value,
            new() { ["theme"] = JsonSerializer.SerializeToElement(sourceText) }, result.Value.Generation.QuestionCount, deadline.Token);
        Assert.Equal(passage, Assert.Single(instance.Value.ContentBlocks).Text);
        AssertResponseSchema(request, responseFormat ?? "json_object", "learner");
        Assert.Equal(maxOutputTokens ?? 8192, request.GetProperty("max_completion_tokens").GetInt32());
        using var input = JsonDocument.Parse(request.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(sourceText, input.RootElement.GetProperty("parameters").GetProperty("theme").GetString());

        definition["unexpected"] = true;
        await Assert.ThrowsAsync<AiGenerationException>(() => provider.GetRequiredService<AiGenerationService>()
            .AuthorAsync("A learning idea", deadline.Token));
    }

    private static void AssertResponseSchema(JsonElement request, string responseFormat, string description)
    {
        var prompt = request.GetProperty("messages")[0].GetProperty("content").GetString()!;
        using var schema = JsonDocument.Parse(prompt.Split("\nOutput JSON schema:\n")[1]);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(description, schema.RootElement.GetRawText());
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
    [InlineData("{\"choices\":[{}]}", 502)]
    [InlineData("""{"choices":[{"message":{"content":"provider secret"},"finish_reason":42}]}""", 502)]
    [InlineData("""{"model":42,"choices":[{"message":{"content":"provider secret"}}]}""", 502)]
    [InlineData("""{"created":"provider secret","choices":[{"message":{"content":"{}"}}]}""", 502)]
    [InlineData("""{"created":9223372036854775807,"choices":[{"message":{"content":"{}"}}]}""", 502)]
    [InlineData("""{"created":1e100,"choices":[{"message":{"content":"{}"}}]}""", 502)]
    [InlineData("""{"choices":[{"message":{"content":[null]}}]}""", 502)]
    [InlineData("""{"choices":[{"message":{"role":"provider secret","content":"{}"}}]}""", 502)]
    [InlineData("""{"choices":[{"message":{"tool_calls":[{"type":"function","function":{"name":"f","arguments":"{}"}}]}}]}""", 502)]
    [InlineData("null", 502)]
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
            var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync("A learning idea", deadline.Token));
            Assert.Equal(status, error.StatusCode);
            Assert.DoesNotContain("provider secret", error.Message);
        }
        Assert.Equal(3, requests);
    }

    [Theory]
    [InlineData("Ai:Temperature", "-1")]
    [InlineData("Ai:Temperature", "NaN")]
    [InlineData("Ai:Temperature", "3")]
    [InlineData("Ai:TopP", "0")]
    [InlineData("Ai:TopP", "2")]
    [InlineData("Ai:TopK", "-1")]
    [InlineData("Ai:ReasoningMaxTokens", "0")]
    [InlineData("Ai:ReasoningMaxTokens", "-1")]
    [InlineData("Ai:ReasoningMaxTokens", "8192")]
    [InlineData("Ai:ReasoningMaxTokens", "4096", null, "4096")]
    [InlineData("Ai:ReasoningMaxTokens", "2048", "low")]
    [InlineData("Ai:ReasoningEffort", "unlimited")]
    [InlineData("Ai:ReasoningEnabled", "maybe")]
    [InlineData("Ai:ResponseFormat", "maybe")]
    [InlineData("Ai:Model", null)]
    [InlineData("Ai:Model", "")]
    [InlineData("Ai:Model", " ")]
    [InlineData("Ai:Model", "paid/model,test:free")]
    [InlineData("Ai:FallbackModel", "paid/model,test:free")]
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
        Assert.Contains(setting, error.Message);
    }
}
