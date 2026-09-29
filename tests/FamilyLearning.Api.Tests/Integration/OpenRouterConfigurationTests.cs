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
    [InlineData(false, "low", 0.7f, 0.8f, "test/secondary:free", true)]
    [InlineData(true, "medium", 1f, 0.95f, "test/secondary:free", true)]
    [InlineData(true, "low", null, null, "", true)]
    [InlineData(null, null, null, null, null, null)]
    [InlineData(true, "", 1f, 0.95f, "", false)]
    [InlineData(true, "", 1f, 0.95f, "", true, "qwen/qwen3.8-flash", 2048, 20)]
    [InlineData(false, "", null, null, "", true, "qwen/qwen3.8-flash", 2048)]
    [InlineData(true, "", null, null, "qwen/qwen3.8-flash", true)]
    public async Task Generation_requests_preserve_settings_schema_guidance_and_unicode(bool? enabled, string? effort,
        float? temperature, float? topP, string? fallbackModel, bool? useJsonSchema, string model = "test/model:free",
        int? reasoningMaxTokens = null, int? topK = null)
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
            ["Ai:UseJsonSchema"] = useJsonSchema?.ToString(),
            ["Ai:FallbackModel"] = fallbackModel,
            ["Ai:ReasoningEnabled"] = enabled?.ToString(),
            ["Ai:ReasoningEffort"] = effort,
            ["Ai:ReasoningMaxTokens"] = reasoningMaxTokens?.ToString(CultureInfo.InvariantCulture),
            ["Ai:Temperature"] = temperature?.ToString(CultureInfo.InvariantCulture),
            ["Ai:TopP"] = topP?.ToString(CultureInfo.InvariantCulture),
            ["Ai:TopK"] = topK?.ToString(CultureInfo.InvariantCulture)
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
        var reasoning = request.GetProperty("reasoning");
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
        if ((enabled ?? true) && reasoningMaxTokens.HasValue)
        {
            Assert.Equal(reasoningMaxTokens, reasoning.GetProperty("max_tokens").GetInt32());
            Assert.False(reasoning.TryGetProperty("effort", out _));
            Assert.False(reasoning.TryGetProperty("enabled", out _));
        }
        else if ((enabled ?? true) && !string.IsNullOrEmpty(effort))
        {
            Assert.Equal(effort, reasoning.GetProperty("effort").GetString());
            Assert.False(reasoning.TryGetProperty("enabled", out _));
        }
        else
        {
            Assert.Equal(enabled ?? true, reasoning.GetProperty("enabled").GetBoolean());
            Assert.False(reasoning.TryGetProperty("effort", out _));
        }
        if (!(enabled ?? true) || !reasoningMaxTokens.HasValue)
            Assert.False(reasoning.TryGetProperty("max_tokens", out _));
        Assert.Equal(temperature, request.TryGetProperty("temperature", out var value) ? value.GetSingle() : null);
        Assert.Equal(topP, request.TryGetProperty("top_p", out value) ? value.GetSingle() : null);
        Assert.Equal(topK, request.TryGetProperty("top_k", out value) ? value.GetInt32() : null);
        Assert.Equal(8192, request.GetProperty("max_completion_tokens").GetInt32());
        Assert.True(request.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        AssertResponseSchema(request, useJsonSchema ?? false, "task generator");

        var instance = await provider.GetRequiredService<AiGenerationService>().GenerateAsync(result.Value,
            new() { ["theme"] = JsonSerializer.SerializeToElement(sourceText), ["count"] = JsonSerializer.SerializeToElement(2) }, deadline.Token);
        Assert.Equal(passage, Assert.Single(instance.Value.ContentBlocks).Text);
        AssertResponseSchema(request, useJsonSchema ?? false, "learner");
        using var input = JsonDocument.Parse(request.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(sourceText, input.RootElement.GetProperty("parameters").GetProperty("theme").GetString());

        definition["unexpected"] = true;
        await Assert.ThrowsAsync<AiGenerationException>(() => provider.GetRequiredService<AiGenerationService>()
            .AuthorAsync("A learning idea", deadline.Token));
    }

    private static void AssertResponseSchema(JsonElement request, bool useJsonSchema, string description)
    {
        var prompt = request.GetProperty("messages")[0].GetProperty("content").GetString()!;
        using var schema = JsonDocument.Parse(prompt.Split("\nOutput JSON schema:\n")[1]);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(description, schema.RootElement.GetRawText());
        var format = request.GetProperty("response_format");
        Assert.Equal(useJsonSchema ? "json_schema" : "json_object", format.GetProperty("type").GetString());
        if (useJsonSchema) Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
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
    [InlineData("Ai:TopK", "0")]
    [InlineData("Ai:TopK", "-1")]
    [InlineData("Ai:ReasoningMaxTokens", "0")]
    [InlineData("Ai:ReasoningMaxTokens", "-1")]
    [InlineData("Ai:ReasoningMaxTokens", "8192")]
    [InlineData("Ai:ReasoningMaxTokens", "2048", "low")]
    [InlineData("Ai:ReasoningEffort", "unlimited")]
    [InlineData("Ai:ReasoningEnabled", "maybe")]
    [InlineData("Ai:UseJsonSchema", "maybe")]
    [InlineData("Ai:Model", null)]
    [InlineData("Ai:Model", "")]
    [InlineData("Ai:Model", " ")]
    [InlineData("Ai:Model", "paid/model,test:free")]
    [InlineData("Ai:FallbackModel", "paid/model,test:free")]
    public void Invalid_generation_settings_are_rejected(string setting, string? value, string? effort = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Model"] = "test/model:free",
            ["Ai:ReasoningEffort"] = effort,
            [setting] = value
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddTaskAi(configuration, new HostingEnvironment()));
        Assert.Contains(setting, error.Message);
    }
}
