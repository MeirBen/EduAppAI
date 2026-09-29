using System.Net;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Adds OpenRouter options and normalizes malformed SDK responses at the provider boundary.</summary>
internal sealed class OpenRouterChatClient(ChatClient client, ChatOptions sampling, BinaryData? reasoning,
    BinaryData? fallbackModels, string responseFormat)
    : DelegatingChatClient(client.AsIChatClient())
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options?.Clone() ?? new();
        options.Temperature = sampling.Temperature;
        options.TopP = sampling.TopP;
        // Every mode retains the prompt's schema and mandatory server validation.
        options.ResponseFormat = responseFormat switch
        {
            "json_schema" => options.ResponseFormat,
            "json_object" => ChatResponseFormat.Json,
            _ => null
        };
        options.RawRepresentationFactory = _ =>
        {
            var request = new ChatCompletionOptions();
#pragma warning disable SCME0001 // The SDK's JSON extension point carries OpenRouter-specific parameters.
            if (reasoning is not null) request.Patch.Set("$.reasoning"u8, reasoning);
            if (sampling.TopK is { } topK) request.Patch.Set("$.top_k"u8, BinaryData.FromObjectAsJson(topK));
            request.Patch.Set("$.provider"u8, BinaryData.FromString("""{"require_parameters":true}"""));
            // With model present, OpenRouter treats models as ordered fallbacks for provider errors.
            if (fallbackModels is not null) request.Patch.Set("$.models"u8, fallbackModels);
#pragma warning restore SCME0001
            return request;
        };
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or
            ArgumentException or NullReferenceException)
        {
            // The SDK can throw non-JSON exceptions for malformed fields or null content parts.
            // Do not retain the exception: even its message can contain provider-controlled content.
            throw new HttpRequestException("OpenRouter returned a malformed completion.", null, HttpStatusCode.BadGateway);
        }
    }
}
