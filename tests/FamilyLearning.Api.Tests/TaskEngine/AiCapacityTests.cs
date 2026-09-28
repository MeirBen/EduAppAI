using System.Collections.Concurrent;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class AiCapacityTests
{
    [Theory]
    [InlineData("cancellation")]
    [InlineData("invalid-json")]
    [InlineData("provider-error")]
    public async Task Failed_requests_release_both_AI_slots(string outcome)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var chat = new PausedChat();
        using var service = new AiGenerationService([chat], NullLogger<AiGenerationService>.Instance);
        var first = service.AuthorAsync("First idea", cancellation.Token);
        var second = service.AuthorAsync("Second idea", cancellation.Token);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        var busy = await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync("Excess", deadline.Token));
        Assert.Equal(503, busy.StatusCode);

        if (outcome == "cancellation")
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(first, second));
            Assert.Equal(2, chat.Tokens.Count);
            Assert.All(chat.Tokens, token => Assert.True(token.IsCancellationRequested));
        }
        else
        {
            if (outcome == "provider-error") chat.Response.SetException(new HttpRequestException("provider secret"));
            else chat.Response.SetResult(Response("not JSON"));
            var error = await Assert.ThrowsAsync<AiGenerationException>(() => Task.WhenAll(first, second));
            Assert.Equal(502, error.StatusCode);
            Assert.DoesNotContain("provider secret", error.Message);
        }

        // Both replacement calls must be accepted before either response completes.
        chat.Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = service.AuthorAsync("Next idea", deadline.Token);
        var another = service.AuthorAsync("Another idea", deadline.Token);
        Assert.False(next.IsCompleted);
        Assert.False(another.IsCompleted);
        chat.Response.SetResult(Response(AiFixtures.Definition().ToJsonString()));
        var results = await Task.WhenAll(next, another);
        Assert.All(results, result => Assert.Equal("קוראים ומגלים", result.Value.Name));
    }

    private static ChatResponse Response(string text) => new(new ChatMessage(ChatRole.Assistant, text))
    { ModelId = "test-free-model", FinishReason = ChatFinishReason.Stop };

    private sealed class PausedChat() : DelegatingChatClient(new AiFixtures.ScriptedChat())
    {
        public TaskCompletionSource<ChatResponse> Response { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<CancellationToken> Tokens { get; } = new();

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Tokens.Enqueue(cancellationToken);
            return Response.Task.WaitAsync(cancellationToken);
        }
    }
}
