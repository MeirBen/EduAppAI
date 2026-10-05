using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.Tests.TaskEngine;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.Tests.Fixtures;

/// <summary>Isolated learning examples and provider responses shared by tests, never by the application.</summary>
internal static class AiFixtures
{
    internal static JsonNode PlanJson() => JsonSerializer.SerializeToNode(LearningPlanFixture.Numeric(), EngineJson.Options)!;

    internal sealed class ScriptedChat(params string[] responses) : IChatClient
    {
        private readonly Queue<string> responses = new(responses);
        public List<(string Input, ChatOptions? Options)> Requests { get; } = [];
        public int DisposeCalls { get; private set; }
        public ChatFinishReason FinishReason { get; init; } = ChatFinishReason.Stop;
        public HttpStatusCode? FailureStatus { get; init; }
        public Func<CancellationToken, Task>? BeforeResponse { get; set; }
        public UsageDetails? Usage { get; set; }
        public string ModelId { get; set; } = "test-free-model";
        public string? ResponseId { get; set; }
        /// <summary>Builds a response from the request text once scripted responses run out.</summary>
        public Func<string, string>? Respond { get; init; }
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var input = string.Join("\n", messages.Select(m => m.Text));
            Requests.Add((input, options));
            if (FailureStatus is { } status) throw new HttpRequestException("provider secret", null, status);
            if (BeforeResponse is not null) await BeforeResponse(cancellationToken);
            var text = responses.Count == 0 && Respond is not null ? Respond(input) : responses.Dequeue();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
            { ModelId = ModelId, ResponseId = ResponseId, FinishReason = FinishReason, Usage = Usage };
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCalls++;
    }
}
