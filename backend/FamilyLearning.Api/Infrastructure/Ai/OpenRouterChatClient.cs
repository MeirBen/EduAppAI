using System.Net;
using System.Text;
using FamilyLearning.Api.TaskEngine.Ai;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Adds OpenRouter options and normalizes malformed SDK responses at the provider boundary.</summary>
internal sealed class OpenRouterChatClient(ChatClient client, ChatOptions sampling, BinaryData? reasoning,
    BinaryData? fallbackModels, BinaryData providerRouting, string responseFormat, int maxSchemaBytes)
    : DelegatingChatClient(client.AsIChatClient())
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options?.Clone() ?? new();
        if (options.ResponseFormat is ChatResponseFormatJson { Schema: { } outputSchema } &&
            Encoding.UTF8.GetByteCount(outputSchema.GetRawText()) > maxSchemaBytes)
            throw AiGenerationException.InputLimit("schema-limit");
        options.Temperature = sampling.Temperature;
        options.TopP = sampling.TopP;
        // Every mode keeps mandatory server validation; only json_schema asks the provider to enforce the schema.
        options.ResponseFormat = responseFormat switch
        {
            "json_schema" => options.ResponseFormat,
            "json_object" => ChatResponseFormat.Json,
            _ => null
        };
        options.RawRepresentationFactory = _ =>
        {
            var request = new ChatCompletionOptions();
            // OpenRouter schemas must retain their bounds; MEAI's OpenAI subset conversion moves them into descriptions.
            if (options.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } format)
                request.ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(
                    format.SchemaName ?? "json_schema", BinaryData.FromString(schema.GetRawText()), format.SchemaDescription,
                    options.AdditionalProperties?.TryGetValue("strict", out var strict) == true && strict is true);
#pragma warning disable SCME0001 // The SDK's JSON extension point carries OpenRouter-specific parameters.
            if (reasoning is not null) request.Patch.Set("$.reasoning"u8, reasoning);
            if (sampling.TopK is { } topK) request.Patch.Set("$.top_k"u8, BinaryData.FromObjectAsJson(topK));
            request.Patch.Set("$.provider"u8, providerRouting);
            // With model present, OpenRouter treats models as ordered fallbacks for provider errors.
            if (fallbackModels is not null) request.Patch.Set("$.models"u8, fallbackModels);
#pragma warning restore SCME0001
            return request;
        };
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            // Keep provider-specific cost extraction at the adapter; absence remains unknown for every caller.
#pragma warning disable SCME0001 // OpenRouter's usage extension is exposed through the SDK JSON patch.
            if (response.RawRepresentation is ChatCompletion { Usage: { } usage } &&
                usage.Patch.TryGetValue("$.cost"u8, out decimal cost) && cost >= 0)
                (response.AdditionalProperties ??= new())["costCredits"] = cost;
#pragma warning restore SCME0001
            return response;
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
