using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.Integration;

public sealed class OpenRouterUsageTests
{
    [Theory]
    [InlineData("{\"cached_tokens\":120,\"cache_write_tokens\":80}", 120L, 80L)]
    [InlineData("{\"cached_tokens\":0,\"cache_write_tokens\":0}", 0L, 0L)]
    [InlineData("{\"cached_tokens\":120}", 120L, null)]
    [InlineData("{\"cache_write_tokens\":80}", null, 80L)]
    [InlineData("{}", null, null)]
    [InlineData("null", null, null)]
    [InlineData("{\"audio_tokens\":0}", null, null)]
    [InlineData("{\"cached_tokens\":-1,\"cache_write_tokens\":-1}", null, null)]
    [InlineData("{\"cached_tokens\":120,\"cache_write_tokens\":null}", 120L, null)]
    [InlineData("{\"cached_tokens\":120,\"cache_write_tokens\":\"80\"}", 120L, null)]
    [InlineData("{\"cached_tokens\":120,\"cache_write_tokens\":1.5}", 120L, null)]
    [InlineData("{\"cached_tokens\":120,\"cache_write_tokens\":9223372036854775808}", 120L, null)]
    public async Task Cache_usage_preserves_reported_counts_without_inventing_missing_measurements(
        string details, long? expectedRead, long? expectedWrite)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        server.MapPost("/chat/completions", () => Results.Json(new
        {
            id = "isolated-usage",
            model = "test/model",
            created = 0,
            choices = new[] { new { index = 0, message = new { role = "assistant", content = GenerationHarness.Questions() }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 200, completion_tokens = 40, total_tokens = 240, cost = 0.01m, prompt_tokens_details = JsonSerializer.Deserialize<JsonElement>(details) }
        }));
        await server.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = "isolated-test-key",
            ["Ai:Model"] = "test/model",
            ["Ai:Endpoint"] = server.Urls.Single()
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddTaskAi(configuration, new HostingEnvironment { EnvironmentName = "Development" });
        using var provider = services.BuildServiceProvider();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var evidence = new AiCallEvidence();
        var result = await provider.GetRequiredService<AiGenerationService>()
            .GenerateQuestionsAsync(new(Resolve(Numeric(1)), []), [], deadline.Token, evidence);

        Assert.Single(result.Value.Questions);
        Assert.NotNull(evidence.Usage);
        Assert.Equal(expectedRead, evidence.Usage.CacheReadTokens);
        Assert.Equal(expectedWrite, evidence.Usage.CacheWriteTokens);
        Assert.Equal(200, evidence.Usage.InputTokens);
        Assert.Equal(0.01m, evidence.Usage.CostCredits);
    }
}
