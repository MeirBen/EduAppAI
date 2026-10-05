using System.ClientModel;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace FamilyLearning.Evaluation;

/// <summary>Sequential evaluation-only observer; the caller owns and disposes the inner provider client.</summary>
/// <remarks>The generator and judge captures share <paramref name="calls"/>, so one budget and current step cover both.</remarks>
internal sealed class EvaluationCapture(IChatClient innerClient, EvaluationCalls calls) : DelegatingChatClient(innerClient)
{

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (calls.Count >= calls.Max) throw new EvaluationCallLimitException();
        var current = calls.Current;
        var request = messages.ToArray();
        current.Request = request.Select(message => new EvaluationMessage(message.Role.Value, message.Text)).ToArray();
        if (request.LastOrDefault()?.Text is { } input)
        {
            try { current.EffectiveInput = JsonSerializer.Deserialize<JsonElement>(input); }
            catch (JsonException) { current.EffectiveInput = JsonSerializer.SerializeToElement(input); }
        }
        current.SchemaName = (options?.ResponseFormat as ChatResponseFormatJson)?.SchemaName;
        current.Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.Clone();
        current.RequestSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current.Request)));
        current.SchemaSha256 = current.Schema is { } schema ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText()))) : null;
        calls.Count++;
        current.RequestSent = true;
        ChatResponse response;
        try { response = await base.GetResponseAsync(request, options, cancellationToken); }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            if (exception.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true &&
                RetryConditionHeaderValue.TryParse(value, out var retryAfter))
                current.RetryAfterSeconds = Math.Max(0,
                    (retryAfter.Delta ?? retryAfter.Date!.Value - DateTimeOffset.UtcNow).TotalSeconds);
            throw;
        }
        current.ResponseReceived = true;
        current.Output = response.Text;
        try { current.Candidate = JsonSerializer.Deserialize<JsonElement>(response.Text); }
        catch (JsonException) { current.Candidate = null; }
        current.Model = response.ModelId;
        current.ResponseId = response.ResponseId;
        current.FinishReason = response.FinishReason?.Value;
        current.InputTokens = response.Usage?.InputTokenCount;
        current.OutputTokens = response.Usage?.OutputTokenCount;
        current.ReasoningTokens = response.Usage?.ReasoningTokenCount;
        if (response.AdditionalProperties?.TryGetValue("costCredits", out var cost) == true && cost is decimal costCredits)
            current.CostCredits = costCredits;
        return response;
    }
}

/// <summary>Calls made so far against the run's limit, and the step the next call records into.</summary>
internal sealed class EvaluationCalls(int max)
{
    internal int Max { get; } = max;
    internal int Count { get; set; }
    internal EvaluationStep Current { get; set; } = new();
}

internal sealed class EvaluationCallLimitException : Exception;
