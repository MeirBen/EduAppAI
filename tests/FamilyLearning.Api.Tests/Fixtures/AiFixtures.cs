using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.Tests.Fixtures;

/// <summary>Isolated learning examples and provider responses shared by tests, never by the application.</summary>
internal static class AiFixtures
{
    internal static JsonNode Definition() => JsonNode.Parse("""
        {"schemaVersion":2,"name":"קוראים ומגלים","instanceParameters":[
          {"key":"theme","label":"נושא","type":"text","required":true,"default":"דינוזאורים","maxLength":100},
          {"key":"count","label":"מספר שאלות","type":"integer","required":true,"default":2,"min":1,"max":20}
        ],"generation":{"instructions":"יש ליצור קטע קריאה בנושא \"theme\" ושאלות עם תשובות קצרות לפי המספר ב־\"count\".","questionCountParameter":"count"}}
        """)!;

    internal static JsonNode Content(string theme = "דינוזאורים", int count = 2) => new JsonObject
    {
        ["title"] = theme,
        ["instructions"] = "קראו וענו",
        ["contentBlocks"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"לומדים על {theme}." }),
        ["questions"] = new JsonArray(Enumerable.Range(1, count).Select(i => (JsonNode)new JsonObject
        {
            ["id"] = $"q{i}",
            ["prompt"] = "מה הנושא?",
            ["points"] = 1,
            ["interaction"] = new JsonObject { ["type"] = "text-input", ["options"] = null },
            ["answer"] = new JsonObject { ["value"] = theme }
        }).ToArray())
    };

    internal sealed class ScriptedChat(params string[] responses) : IChatClient
    {
        private readonly Queue<string> responses = new(responses);
        public List<(string Input, ChatOptions? Options)> Requests { get; } = [];
        public int DisposeCalls { get; private set; }
        public ChatFinishReason FinishReason { get; init; } = ChatFinishReason.Stop;
        public HttpStatusCode? FailureStatus { get; init; }
        public Func<CancellationToken, Task>? BeforeResponse { get; init; }
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add((string.Join("\n", messages.Select(m => m.Text)), options));
            if (FailureStatus is { } status) throw new HttpRequestException("provider secret", null, status);
            if (BeforeResponse is not null) await BeforeResponse(cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, responses.Dequeue()))
            { ModelId = "test-free-model", FinishReason = FinishReason };
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCalls++;
    }
}
