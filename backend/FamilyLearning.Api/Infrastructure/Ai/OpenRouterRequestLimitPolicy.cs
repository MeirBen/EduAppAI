using System.ClientModel.Primitives;
using FamilyLearning.Api.TaskEngine.Ai;

namespace FamilyLearning.Api.Infrastructure.Ai;

/// <summary>Checks the SDK-compiled HTTP body before transport, including schema copies and provider options.</summary>
internal sealed class OpenRouterRequestLimitPolicy(int maxBytes) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (message.Request.Content is { } content)
        {
            if (!content.TryComputeLength(out var length))
            {
                using var buffer = new MemoryStream();
                content.WriteTo(buffer, message.CancellationToken);
                length = buffer.Length;
            }
            Check(length);
        }
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (message.Request.Content is { } content)
        {
            if (!content.TryComputeLength(out var length))
            {
                using var buffer = new MemoryStream();
                await content.WriteToAsync(buffer, message.CancellationToken);
                length = buffer.Length;
            }
            Check(length);
        }
        await ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Check(long length)
    {
        if (length > maxBytes) throw AiGenerationException.InputLimit("request-limit");
    }
}
