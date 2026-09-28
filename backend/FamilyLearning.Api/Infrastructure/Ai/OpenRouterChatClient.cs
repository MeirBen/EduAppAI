using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Adds OpenRouter options to non-streaming generation requests.</summary>
internal sealed class OpenRouterChatClient(ChatClient client, ChatOptions sampling, BinaryData reasoning)
    : DelegatingChatClient(client.AsIChatClient())
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options?.Clone() ?? new();
        options.Temperature = sampling.Temperature;
        options.TopP = sampling.TopP;
        options.RawRepresentationFactory = _ =>
        {
            var request = new ChatCompletionOptions();
#pragma warning disable SCME0001 // The SDK's JSON extension point carries OpenRouter-specific parameters.
            request.Patch.Set("$.reasoning"u8, reasoning);
            request.Patch.Set("$.provider"u8, BinaryData.FromString("""{"require_parameters":true}"""));
#pragma warning restore SCME0001
            return request;
        };
        return base.GetResponseAsync(messages, options, cancellationToken);
    }
}
