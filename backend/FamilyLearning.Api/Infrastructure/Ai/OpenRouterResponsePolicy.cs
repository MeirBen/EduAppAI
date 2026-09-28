using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Rejects failed non-streaming completions before the OpenAI SDK interprets them.</summary>
internal sealed class OpenRouterResponsePolicy : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        Validate(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex);
        Validate(message);
    }

    private static void Validate(PipelineMessage message)
    {
        if (!message.BufferResponse || message.Response is not { Status: 200 } response) return;
        // OpenRouter can report generation errors inside HTTP 200; the SDK assumes a completion exists.
        try
        {
            using var document = JsonDocument.Parse(response.Content.ToMemory());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Failure();
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                var rateLimited = error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number &&
                    code.TryGetInt32(out var status) && status == 429;
                throw Failure(rateLimited ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway);
            }
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object ||
                !choices[0].TryGetProperty("message", out var completion) || completion.ValueKind != JsonValueKind.Object)
                throw Failure();
        }
        catch (JsonException) { throw Failure(); }
    }

    // Never copy provider error text: it can include prompts, answers or credentials.
    private static HttpRequestException Failure(HttpStatusCode status = HttpStatusCode.BadGateway) =>
        new("OpenRouter returned a failed or malformed completion.", null, status);
}
