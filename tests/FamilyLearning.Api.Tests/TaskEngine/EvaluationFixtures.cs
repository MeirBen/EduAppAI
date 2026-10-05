using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.Tests.TaskEngine;

internal static class EvaluationFixtures
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static LearningPlan Plan(int count = 2) => LearningPlanFixture.Numeric(count) with
    {
        Defaults = new("דינוזאורים", "כיתה ג׳", "easy", count),
        Questions = LearningPlanFixture.Numeric().Questions with { Formats = ["text-input"] }
    };
    internal static JsonNode Definition() => JsonNode.Parse(StructuredEvaluationTests.Proposal(Plan()))!;
    internal static JsonNode Content(int count = 2) => JsonSerializer.SerializeToNode(new QuestionCandidateBatch("דינוזאורים", "קראו וענו",
        Enumerable.Range(0, count).Select(_ => new QuestionCandidate("מה הנושא?", new("text-input"), new("דינוזאורים"), 1)).ToArray()), Json)!;

    internal static string MaterialIdeas() => JsonSerializer.Serialize(new
    {
        ideas = Enumerable.Range(0, 5).Select(index => new
        {
            idea = new { premise = $"רעיון {index}", structure = $"מבנה {index}" },
            recentOverlap = index == 2 ? 0 : 50
        })
    }, Json);

    internal sealed class Chat(Func<int, JsonElement, string> respond) : IChatClient
    {
        public int Calls { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = JsonSerializer.Deserialize<JsonElement>(messages.Last().Text);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(Calls++, input)))
            { ModelId = "isolated-evaluation", FinishReason = ChatFinishReason.Stop });
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
