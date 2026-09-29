using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace FamilyLearning.Evaluation;

/// <summary>Sequential evaluation-only observer; the caller owns and disposes the inner provider client.</summary>
internal sealed class EvaluationCapture(IChatClient innerClient, int maxCalls) : DelegatingChatClient(innerClient)
{
    private int calls;
    internal EvaluationStep Current { get; set; } = new();

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (calls >= maxCalls) throw new EvaluationCallLimitException();
        var request = messages.ToArray();
        Current.Request = request.Select(message => new EvaluationMessage(message.Role.Value, message.Text)).ToArray();
        Current.SchemaName = (options?.ResponseFormat as ChatResponseFormatJson)?.SchemaName;
        calls++;
        Current.RequestSent = true;
        var response = await base.GetResponseAsync(request, options, cancellationToken);
        Current.Output = response.Text;
        Current.Model = response.ModelId;
        Current.ResponseId = response.ResponseId;
        Current.FinishReason = response.FinishReason?.Value;
        Current.InputTokens = response.Usage?.InputTokenCount;
        Current.OutputTokens = response.Usage?.OutputTokenCount;
        Current.ReasoningTokens = response.Usage?.ReasoningTokenCount;
        // OpenRouter returns usage.cost automatically. Never infer missing costs as free calls.
#pragma warning disable SCME0001 // The SDK preserves provider-specific usage fields in its JSON patch.
        if (response.RawRepresentation is ChatCompletion { Usage: { } usage } &&
            usage.Patch.TryGetValue("$.cost"u8, out decimal cost) && cost >= 0)
            Current.CostCredits = cost;
#pragma warning restore SCME0001
        return response;
    }
}

internal sealed class EvaluationCallLimitException : Exception;
