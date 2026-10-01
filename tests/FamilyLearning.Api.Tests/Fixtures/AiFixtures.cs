using System.Net;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.Tests.Fixtures;

/// <summary>Isolated learning examples and provider responses shared by tests, never by the application.</summary>
internal static class AiFixtures
{
    internal static JsonNode Definition() => JsonNode.Parse("""
        {"schemaVersion":4,"name":"קוראים ומגלים","instanceParameters":[
          {"key":"sourceText","label":"טקסט מקור","type":"text","required":true,"default":"דינוזאורים","maxLength":100}
        ],"generation":{"instructions":"יש ליצור שאלות עם תשובות קצרות לפי הטקסט \"sourceText\".","defaults":{"topic":"דינוזאורים","audience":"כיתה ג׳","difficulty":"easy","questionCount":2}}}
        """)!;

    internal static TaskSettings Settings(int count = 2) => new("דינוזאורים", "כיתה ג׳", "easy", count);

    internal static TaskInput Input(int count = 2) => new(Settings(count), []);

    internal static JsonNode Content(string sourceText = "דינוזאורים", int count = 2) => new JsonObject
    {
        ["title"] = sourceText,
        ["instructions"] = "קראו וענו",
        ["contentBlocks"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"לומדים על {sourceText}." }),
        ["questions"] = new JsonArray(Enumerable.Range(1, count).Select(i => (JsonNode)new JsonObject
        {
            ["id"] = $"q{i}",
            ["prompt"] = "מה הנושא?",
            ["points"] = 1,
            ["interaction"] = new JsonObject { ["type"] = "text-input", ["options"] = null },
            ["answer"] = new JsonObject { ["value"] = sourceText }
        }).ToArray())
    };

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
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add((string.Join("\n", messages.Select(m => m.Text)), options));
            if (FailureStatus is { } status) throw new HttpRequestException("provider secret", null, status);
            if (BeforeResponse is not null) await BeforeResponse(cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, responses.Dequeue()))
            { ModelId = ModelId, ResponseId = ResponseId, FinishReason = FinishReason, Usage = Usage };
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCalls++;
    }
}
