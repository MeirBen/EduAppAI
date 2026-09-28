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
    [InlineData(false, "low", 0.7f, 0.8f, "test/secondary:free")]
    [InlineData(true, "medium", 1f, 0.95f, "test/secondary:free")]
    [InlineData(true, "low", null, null, "")]
    [InlineData(null, null, null, null, null)]
    public async Task Configured_reasoning_and_sampling_reach_the_provider(bool? enabled, string? effort,
        float? temperature, float? topP, string? fallbackModel)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        JsonElement request = default;
        server.MapPost("/chat/completions", async (HttpRequest incoming) =>
        {
            using var body = await JsonDocument.ParseAsync(incoming.Body);
            request = body.RootElement.Clone();
            return Results.Json(new
            {
                id = "local-test",
                model = "test:free",
                created = 0,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = AiFixtures.Definition().ToJsonString() }, finish_reason = "stop" } }
            });
        });
        await server.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Endpoint"] = server.Urls.Single(),
            ["Ai:FallbackModel"] = fallbackModel,
            ["Ai:ReasoningEnabled"] = enabled?.ToString(),
            ["Ai:ReasoningEffort"] = effort,
            ["Ai:Temperature"] = temperature?.ToString(CultureInfo.InvariantCulture),
            ["Ai:TopP"] = topP?.ToString(CultureInfo.InvariantCulture)
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddTaskAi(configuration, new HostingEnvironment { EnvironmentName = "Development" });
        using var provider = services.BuildServiceProvider();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await provider.GetRequiredService<AiGenerationService>().AuthorAsync("A learning idea", deadline.Token);

        Assert.Equal("nvidia/nemotron-3-super-120b-a12b:free", request.GetProperty("model").GetString());
        if (string.IsNullOrWhiteSpace(fallbackModel)) Assert.False(request.TryGetProperty("models", out _));
        else Assert.Equal(fallbackModel, Assert.Single(request.GetProperty("models").EnumerateArray()).GetString());
        Assert.Equal("test:free", result.Metadata.Model);
        var reasoning = request.GetProperty("reasoning");
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
        if (enabled ?? true)
        {
            Assert.Equal(effort ?? "low", reasoning.GetProperty("effort").GetString());
            Assert.False(reasoning.TryGetProperty("enabled", out _));
        }
        else
        {
            Assert.False(reasoning.GetProperty("enabled").GetBoolean());
            Assert.False(reasoning.TryGetProperty("effort", out _));
        }
        Assert.Equal(temperature, request.TryGetProperty("temperature", out var value) ? value.GetSingle() : null);
        Assert.Equal(topP, request.TryGetProperty("top_p", out value) ? value.GetSingle() : null);
        Assert.Equal("json_schema", request.GetProperty("response_format").GetProperty("type").GetString());
        Assert.True(request.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":429,\"message\":\"provider secret\"}}", 429)]
    [InlineData("{\"error\":{\"code\":503,\"message\":\"provider secret\"}}", 502)]
    [InlineData("{\"choices\":[]}", 502)]
    [InlineData("{\"choices\":[{}]}", 502)]
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
    [InlineData("Ai:ReasoningEffort", "unlimited")]
    [InlineData("Ai:Model", "paid/model")]
    [InlineData("Ai:FallbackModel", "paid/model")]
    [InlineData("Ai:FallbackModel", "paid/model,test:free")]
    public void Invalid_generation_settings_are_rejected(string setting, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            [setting] = value
        }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddTaskAi(configuration, new HostingEnvironment()));
    }
}
