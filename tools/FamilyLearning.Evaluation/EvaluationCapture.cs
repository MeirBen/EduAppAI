using System.ClientModel;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace FamilyLearning.Evaluation;

/// <summary>Sequential evaluation-only observer; the caller owns and disposes the inner provider client.</summary>
internal sealed class EvaluationCapture(IChatClient innerClient, int maxCalls, EvaluationReport? budgetReport = null) : DelegatingChatClient(innerClient)
{
    private int calls;
    internal EvaluationStep Current { get; set; } = new();

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (calls >= maxCalls) throw new EvaluationCallLimitException();
        var request = messages.ToArray();
        decimal reservedCost = 0;
        if (budgetReport?.Experiment is { } experiment)
        {
            reservedCost = experiment.Reserve(request, options);
            if (budgetReport.ReservedCostUsd + reservedCost > experiment.MaxCostUsd) throw new EvaluationCostLimitException();
            budgetReport.ReservedCostUsd += reservedCost;
        }
        Current.Request = request.Select(message => new EvaluationMessage(message.Role.Value, message.Text)).ToArray();
        Current.SchemaName = (options?.ResponseFormat as ChatResponseFormatJson)?.SchemaName;
        Current.Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.Clone();
        Current.RequestSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Current.Request)));
        Current.SchemaSha256 = Current.Schema is { } schema ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText()))) : null;
        calls++;
        Current.RequestSent = true;
        ChatResponse response;
        try { response = await base.GetResponseAsync(request, options, cancellationToken); }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            if (exception.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true &&
                RetryConditionHeaderValue.TryParse(value, out var retryAfter))
                Current.RetryAfterSeconds = Math.Max(0,
                    (retryAfter.Delta ?? retryAfter.Date!.Value - DateTimeOffset.UtcNow).TotalSeconds);
            throw;
        }
        Current.ResponseReceived = true;
        Current.Output = response.Text;
        Current.Model = response.ModelId;
        Current.ResponseId = response.ResponseId;
        Current.FinishReason = response.FinishReason?.Value;
        Current.InputTokens = response.Usage?.InputTokenCount;
        Current.OutputTokens = response.Usage?.OutputTokenCount;
        Current.ReasoningTokens = response.Usage?.ReasoningTokenCount;
        if (response.AdditionalProperties?.TryGetValue("costCredits", out var cost) == true && cost is decimal costCredits)
            Current.CostCredits = costCredits;
        if (budgetReport?.Experiment is not null && Current.CostCredits is { } actualCost)
            budgetReport.ReservedCostUsd += actualCost - reservedCost;
        return response;
    }
}

internal sealed class EvaluationCallLimitException : Exception;
