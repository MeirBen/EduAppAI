using System.Collections.Concurrent;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class AiCapacityTests
{
    [Fact]
    public async Task Interactive_callers_never_queue_while_background_work_waits_for_a_released_slot()
    {
        using var capacity = new AiCapacity();
        var first = capacity.TryEnter();
        using var second = capacity.TryEnter();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(capacity.TryEnter());
        using (var cancelled = new CancellationTokenSource())
        {
            var abandoned = capacity.EnterAsync(cancelled.Token);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        }
        var waiting = capacity.EnterAsync(default);
        Assert.False(waiting.IsCompleted);
        first!.Dispose();
        first.Dispose();
        using var background = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        // A slot released twice frees one place only, so the budget stays at two.
        Assert.Null(capacity.TryEnter());
    }

    [Theory]
    [InlineData("cancellation")]
    [InlineData("invalid-json")]
    [InlineData("provider-error")]
    [InlineData("timeout")]
    public async Task Failed_requests_end_with_safe_bounded_errors(string outcome)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var chat = new PausedChat();
        using var services = CreateServices(chat, "1");
        var request = services.GetRequiredService<AiGenerationService>().AuthorAsync(new ActivityAuthoringInput("רעיון"), cancellation.Token);
        Assert.False(request.IsCompleted);
        if (outcome == "cancellation")
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        }
        else if (outcome == "timeout")
            Assert.Equal(504, (await Assert.ThrowsAsync<AiGenerationException>(() => request.WaitAsync(deadline.Token))).StatusCode);
        else
        {
            if (outcome == "provider-error") chat.Response.SetException(new HttpRequestException("provider secret"));
            else chat.Response.SetResult(Response("not JSON"));
            var error = await Assert.ThrowsAsync<AiGenerationException>(() => request);
            Assert.Equal(502, error.StatusCode);
            Assert.DoesNotContain("provider secret", error.Message);
        }
        if (outcome is "cancellation" or "timeout") Assert.True(Assert.Single(chat.Tokens).IsCancellationRequested);
    }

    [Theory]
    [InlineData("0", "8192")]
    [InlineData("180", "32769")]
    public void Invalid_generation_limits_are_rejected(string seconds, string maxOutputTokens)
    {
        using var chat = new PausedChat();
        using var services = CreateServices(chat, seconds, maxOutputTokens);
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<AiGenerationService>());
    }

    private static ServiceProvider CreateServices(IChatClient chat, string seconds, string maxOutputTokens = "8192")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:RequestTimeoutSeconds"] = seconds,
            ["Ai:MaxOutputTokens"] = maxOutputTokens
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(chat);
        services.AddTaskAi(configuration, new HostingEnvironment());
        return services.BuildServiceProvider();
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
