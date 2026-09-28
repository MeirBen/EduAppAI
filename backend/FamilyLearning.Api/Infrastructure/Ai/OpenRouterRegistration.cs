using System.ClientModel;
using System.ClientModel.Primitives;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>OpenRouter is a replaceable transport; generation depends only on IChatClient.</summary>
public static class OpenRouterRegistration
{
    /// <summary>Registers the free-only provider when a server secret exists; otherwise AI stays explicitly unavailable.</summary>
    public static void AddTaskAi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<AiGenerationOptions>()
            .Bind(configuration.GetSection("Ai"))
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 300,
                "Ai:RequestTimeoutSeconds must be between 1 and 300.")
            .ValidateOnStart();
        services.AddSingleton<AiGenerationService>();
        var key = configuration["Ai:ApiKey"] ?? configuration["OPENROUTER_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) return;
        var model = configuration["Ai:Model"] ?? "qwen/qwen3.8-27b:free";
        var reasoningEnabled = configuration.GetValue<bool>("Ai:ReasoningEnabled");
        if (model != "openrouter/free" && (!model.EndsWith(":free", StringComparison.Ordinal) || model.Contains(',')))
            throw new InvalidOperationException("Ai:Model must be openrouter/free or a single :free model.");
        var endpoint = new Uri(configuration["Ai:Endpoint"] ?? "https://openrouter.ai/api/v1");
        // Local endpoints support isolated provider-contract tests without exposing real keys or paying for calls.
        if (endpoint.AbsoluteUri.TrimEnd('/') != "https://openrouter.ai/api/v1" &&
            !(environment.IsDevelopment() && endpoint.IsLoopback && endpoint.Scheme == "http"))
            throw new InvalidOperationException("Ai:Endpoint must be OpenRouter, or a loopback HTTP endpoint in Development.");
        services.AddSingleton<IChatClient>(provider => new OpenRouterChatClient(new ChatClient(model, new ApiKeyCredential(key), new OpenAIClientOptions
        {
            Endpoint = endpoint,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            // Let the application deadline cancel first so timeouts consistently return 504.
            NetworkTimeout = provider.GetRequiredService<IOptions<AiGenerationOptions>>().Value.RequestTimeout + TimeSpan.FromSeconds(5)
        }), reasoningEnabled));
    }
}
