using System.ClientModel;
using System.ClientModel.Primitives;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>OpenRouter is a replaceable transport; generation depends only on IChatClient.</summary>
public static class OpenRouterRegistration
{
    /// <summary>Registers the free-only provider when a server secret exists; otherwise AI stays explicitly unavailable.</summary>
    public static void AddTaskAi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<AiGenerationService>();
        var key = configuration["Ai:ApiKey"] ?? configuration["OPENROUTER_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) return;
        var model = configuration["Ai:Model"] ?? "openrouter/free";
        if (model != "openrouter/free" && (!model.EndsWith(":free", StringComparison.Ordinal) || model.Contains(',')))
            throw new InvalidOperationException("Ai:Model must be openrouter/free or a single :free model.");
        var endpoint = new Uri(configuration["Ai:Endpoint"] ?? "https://openrouter.ai/api/v1");
        // Local endpoints support isolated provider-contract tests without exposing real keys or paying for calls.
        if (endpoint.AbsoluteUri.TrimEnd('/') != "https://openrouter.ai/api/v1" &&
            !(environment.IsDevelopment() && endpoint.IsLoopback && endpoint.Scheme == "http"))
            throw new InvalidOperationException("Ai:Endpoint must be OpenRouter, or a loopback HTTP endpoint in Development.");
        services.AddSingleton<IChatClient>(_ => new ChatClient(model, new ApiKeyCredential(key), new OpenAIClientOptions
        {
            Endpoint = endpoint,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            NetworkTimeout = TimeSpan.FromSeconds(60)
        }).AsIChatClient());
    }
}
