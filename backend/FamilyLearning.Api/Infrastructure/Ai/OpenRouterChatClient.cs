using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Adds OpenRouter options to non-streaming generation requests.</summary>
internal sealed class OpenRouterChatClient(ChatClient client) : DelegatingChatClient(client.AsIChatClient())
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options?.Clone() ?? new();
        options.RawRepresentationFactory = _ =>
        {
            var request = new ChatCompletionOptions();
            // Some free models require reasoning. Request less effort without disabling it;
            // exclude only keeps reasoning out of the response, it does not limit computation.
#pragma warning disable SCME0001 // The SDK's JSON extension point carries OpenRouter-specific parameters.
            request.Patch.Set("$.reasoning"u8, BinaryData.FromString("""{"effort":"low","exclude":true}"""));
#pragma warning restore SCME0001
            return request;
        };
        return base.GetResponseAsync(messages, options, cancellationToken);
    }
}
