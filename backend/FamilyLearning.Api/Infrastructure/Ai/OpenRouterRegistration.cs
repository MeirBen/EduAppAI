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
        var model = configuration["Ai:Model"];
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ai:Model is required when an AI API key is configured.");
        var fallbackModel = configuration["Ai:FallbackModel"];
        if (string.IsNullOrWhiteSpace(fallbackModel) || fallbackModel == model) fallbackModel = null;
        var reasoningEnabled = configuration.GetValue("Ai:ReasoningEnabled", true);
        var effort = configuration["Ai:ReasoningEffort"] ?? "";
        if (effort is not ("" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidOperationException("Ai:ReasoningEffort must be empty, minimal, low, medium, high, xhigh or max.");
        var useJsonSchema = configuration.GetValue("Ai:UseJsonSchema", false);
        var sampling = new ChatOptions
        {
            Temperature = configuration.GetValue<float?>("Ai:Temperature"),
            TopP = configuration.GetValue<float?>("Ai:TopP")
        };
        if (sampling.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature is < 0 or > 2))
            throw new InvalidOperationException("Ai:Temperature must be between 0 and 2.");
        if (sampling.TopP is { } topP && (!float.IsFinite(topP) || topP is <= 0 or > 1))
            throw new InvalidOperationException("Ai:TopP must be greater than 0 and at most 1.");
        object reasoning = reasoningEnabled && effort.Length > 0
            ? new { effort, exclude = true } : new { enabled = reasoningEnabled, exclude = true };
        if (!IsFreeModel(model) || (fallbackModel is not null && !IsFreeModel(fallbackModel)))
            throw new InvalidOperationException("Ai:Model and Ai:FallbackModel must each be openrouter/free or a single :free model.");
        var endpoint = new Uri(configuration["Ai:Endpoint"] ?? "https://openrouter.ai/api/v1");
        // Local endpoints support isolated provider-contract tests without exposing real keys or paying for calls.
        if (endpoint.AbsoluteUri.TrimEnd('/') != "https://openrouter.ai/api/v1" &&
            !(environment.IsDevelopment() && endpoint.IsLoopback && endpoint.Scheme == "http"))
            throw new InvalidOperationException("Ai:Endpoint must be OpenRouter, or a loopback HTTP endpoint in Development.");
        services.AddSingleton<IChatClient>(provider =>
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = endpoint,
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                // Let the application deadline cancel first so timeouts consistently return 504.
                NetworkTimeout = provider.GetRequiredService<IOptions<AiGenerationOptions>>().Value.RequestTimeout + TimeSpan.FromSeconds(5)
            };
            clientOptions.AddPolicy(new OpenRouterResponsePolicy(), PipelinePosition.PerCall);
            return new OpenRouterChatClient(new ChatClient(model, new ApiKeyCredential(key), clientOptions),
                sampling, BinaryData.FromObjectAsJson(reasoning),
                fallbackModel is null ? null : BinaryData.FromObjectAsJson(new[] { fallbackModel }), useJsonSchema);
        });
    }

    private static bool IsFreeModel(string model) => model == "openrouter/free" ||
        (model.EndsWith(":free", StringComparison.Ordinal) && !model.Contains(','));
}
