using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Registers the OpenRouter adapter behind <see cref="IChatClient"/> when a server secret exists; otherwise AI stays unavailable.</summary>
public static class OpenRouterRegistration
{
    /// <summary>An optional transport lets isolated developer probes guard the unchanged production wire contract.</summary>
    public static void AddTaskAi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment,
        PipelineTransport? transport = null)
    {
        services.AddOptions<AiGenerationOptions>()
            .Bind(configuration.GetSection("Ai"))
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 300,
                "Ai:RequestTimeoutSeconds must be between 1 and 300.")
            .Validate(options => options.MaxOutputTokens is >= 1 and <= 32768,
                "Ai:MaxOutputTokens must be between 1 and 32768.")
            .Validate(options => options.MaxRequestBytes is >= 1 and <= AiGenerationOptions.RequestByteLimit,
                "Ai:MaxRequestBytes must be positive and at most 512 KiB.")
            .Validate(options => options.MaxSchemaBytes is >= 1 and <= AiGenerationOptions.SchemaByteLimit,
                "Ai:MaxSchemaBytes must be positive and at most 64 KiB.")
            .Validate(options => options.StrictQuestionCountLimit is >= 0 and <= EngineValidation.MaxQuestionCount,
                $"Ai:StrictQuestionCountLimit must be between 0 and {EngineValidation.MaxQuestionCount}.")
            .ValidateOnStart();
        services.AddSingleton<AiGenerationService>();
        services.AddSingleton<AiCapacity>();
        var key = configuration["Ai:ApiKey"] ?? configuration["OPENROUTER_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) return;
        var model = configuration["Ai:Model"];
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ai:Model is required when an AI API key is configured.");
        var fallbackModel = configuration["Ai:FallbackModel"];
        if (string.IsNullOrWhiteSpace(fallbackModel) || fallbackModel == model) fallbackModel = null;
        var ignoredProviders = configuration.GetSection("Ai:IgnoredProviders").Get<string[]>() ?? [];
        if (ignoredProviders.Length > 16 || ignoredProviders.Any(slug => slug is null || slug.Length is < 1 or > 64 ||
            slug.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('-' or '_' or '/'))))
            throw new InvalidOperationException("Ai:IgnoredProviders accepts at most 16 provider slugs of 1–64 lowercase letters, digits, hyphens, underscores or slashes.");
        // OpenRouter refuses a request rather than route it above this USD-per-million-token ceiling.
        var promptPrice = configuration.GetValue<decimal?>("Ai:MaxPrice:PromptPerMillion");
        var completionPrice = configuration.GetValue<decimal?>("Ai:MaxPrice:CompletionPerMillion");
        if (promptPrice.HasValue != completionPrice.HasValue || promptPrice <= 0 || completionPrice <= 0)
            throw new InvalidOperationException("Ai:MaxPrice:PromptPerMillion and Ai:MaxPrice:CompletionPerMillion must be set together as positive prices.");
        var routing = new JsonObject { ["require_parameters"] = true };
        if (ignoredProviders.Length > 0) routing["ignore"] = JsonSerializer.SerializeToNode(ignoredProviders);
        if (promptPrice.HasValue) routing["max_price"] = new JsonObject { ["prompt"] = promptPrice, ["completion"] = completionPrice };
        var providerRouting = BinaryData.FromObjectAsJson(routing);
        var reasoningEnabled = configuration.GetValue<bool?>("Ai:ReasoningEnabled");
        var effort = configuration["Ai:ReasoningEffort"] ?? "";
        if (effort is not ("" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidOperationException("Ai:ReasoningEffort must be empty, minimal, low, medium, high, xhigh or max.");
        var reasoningMaxTokens = configuration.GetValue<int?>("Ai:ReasoningMaxTokens");
        var maxOutputTokens = configuration.GetValue("Ai:MaxOutputTokens", AiGenerationOptions.DefaultMaxOutputTokens);
        if (reasoningMaxTokens is <= 0 || reasoningMaxTokens >= maxOutputTokens)
            throw new InvalidOperationException("Ai:ReasoningMaxTokens must be positive and below Ai:MaxOutputTokens.");
        if (reasoningMaxTokens.HasValue && effort.Length > 0)
            throw new InvalidOperationException("Set either Ai:ReasoningMaxTokens or Ai:ReasoningEffort, not both.");
        if (configuration["Ai:ResponseFormat"] is not (null or "json_schema" or "json_object" or "text"))
            throw new InvalidOperationException("Ai:ResponseFormat must be json_schema, json_object or text.");
        var sampling = new ChatOptions
        {
            Temperature = configuration.GetValue<float?>("Ai:Temperature"),
            TopP = configuration.GetValue<float?>("Ai:TopP"),
            TopK = configuration.GetValue<int?>("Ai:TopK")
        };
        if (sampling.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature is < 0 or > 2))
            throw new InvalidOperationException("Ai:Temperature must be between 0 and 2.");
        if (sampling.TopP is { } topP && (!float.IsFinite(topP) || topP is <= 0 or > 1))
            throw new InvalidOperationException("Ai:TopP must be greater than 0 and at most 1.");
        if (sampling.TopK is < 0)
            throw new InvalidOperationException("Ai:TopK must be nonnegative.");
        // Omit unset controls; a budget or effort enables reasoning unless explicitly disabled.
        object? reasoning = reasoningEnabled.HasValue ? new { enabled = reasoningEnabled.Value, exclude = true } : null;
        if (reasoningEnabled != false && reasoningMaxTokens.HasValue)
            reasoning = new { max_tokens = reasoningMaxTokens.Value, exclude = true };
        else if (reasoningEnabled != false && effort.Length > 0)
            reasoning = new { effort, exclude = true };
        if (model.Contains(',') || fallbackModel?.Contains(',') == true)
            throw new InvalidOperationException("Ai:Model and Ai:FallbackModel must each contain a single model ID.");
        var endpoint = new Uri(configuration["Ai:Endpoint"] ?? "https://openrouter.ai/api/v1");
        // Local endpoints support isolated provider-contract tests without exposing real keys or paying for calls.
        if (endpoint.AbsoluteUri.TrimEnd('/') != "https://openrouter.ai/api/v1" &&
            !(environment.IsDevelopment() && endpoint.IsLoopback && endpoint.Scheme == "http"))
            throw new InvalidOperationException("Ai:Endpoint must be OpenRouter, or a loopback HTTP endpoint in Development.");
        services.AddSingleton<IChatClient>(provider =>
        {
            var limits = provider.GetRequiredService<IOptions<AiGenerationOptions>>().Value;
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = endpoint,
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                // Let the application deadline cancel first so timeouts consistently return 504.
                NetworkTimeout = limits.RequestTimeout + TimeSpan.FromSeconds(5)
            };
            if (transport is not null) clientOptions.Transport = transport;
            clientOptions.AddPolicy(new OpenRouterRequestLimitPolicy(limits.MaxRequestBytes), PipelinePosition.PerCall);
            clientOptions.AddPolicy(new OpenRouterResponsePolicy(), PipelinePosition.PerCall);
            return new OpenRouterChatClient(new ChatClient(model, new ApiKeyCredential(key), clientOptions),
                sampling, reasoning is null ? null : BinaryData.FromObjectAsJson(reasoning),
                fallbackModel is null ? null : BinaryData.FromObjectAsJson(new[] { fallbackModel }), providerRouting, limits.ResponseFormat, limits.MaxSchemaBytes);
        });
    }
}
