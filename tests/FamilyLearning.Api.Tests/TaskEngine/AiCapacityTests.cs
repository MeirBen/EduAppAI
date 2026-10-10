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
    public async Task Scoped_content_and_authoring_share_capacity_and_cancellation_releases_it()
    {
        using var chat = new PausedChat();
        using var services = CreateServices(chat, "5");
        var service = services.GetRequiredService<AiGenerationService>();
        var request = LearningPlanFixture.Resolve(LearningPlanFixture.Numeric());
        using var cancellation = new CancellationTokenSource();
        var author = service.AuthorAsync(new ActivityAuthoringInput("רעיון"), cancellation.Token);
        var content = service.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(request, TaskAssembly.CreateDocument(request)), [], cancellation.Token);
        var error = await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new ActivityAuthoringInput("עוד"), default));
        Assert.Equal(503, error.StatusCode);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(author, content));
        chat.Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = service.AuthorAsync(new ActivityAuthoringInput("שוב"), default);
        chat.Response.SetResult(Response("""{"result":{"proposal":null,"clarification":"איזה גיל?"},"assumptions":[]}"""));
        Assert.Equal("איזה גיל?", (await next).Value.Reply);
    }

    [Theory]
    [InlineData("cancellation")]
    [InlineData("invalid-json")]
    [InlineData("provider-error")]
    [InlineData("timeout")]
    public async Task Failed_requests_release_both_AI_slots(string outcome)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var chat = new PausedChat();
        using var services = CreateServices(chat, "1");
        var service = services.GetRequiredService<AiGenerationService>();
        var first = service.AuthorAsync(new ActivityAuthoringInput("First idea"), cancellation.Token);
        var second = service.AuthorAsync(new ActivityAuthoringInput("Second idea"), cancellation.Token);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        var busy = await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new ActivityAuthoringInput("Excess"), deadline.Token));
        Assert.Equal(503, busy.StatusCode);

        if (outcome is "cancellation" or "timeout")
        {
            if (outcome == "cancellation")
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(first, second));
            }
            else
            {
                var error = await Assert.ThrowsAsync<AiGenerationException>(() => Task.WhenAll(first, second).WaitAsync(deadline.Token));
                Assert.Equal(504, error.StatusCode);
            }
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
        var next = service.AuthorAsync(new ActivityAuthoringInput("Next idea"), deadline.Token);
        var another = service.AuthorAsync(new ActivityAuthoringInput("Another idea"), deadline.Token);
        Assert.False(next.IsCompleted);
        Assert.False(another.IsCompleted);
        chat.Response.SetResult(Response("""{"result":{"proposal":null,"clarification":"איזה גיל?"},"assumptions":[]}"""));
        var results = await Task.WhenAll(next, another);
        Assert.All(results, result => Assert.Equal("איזה גיל?", result.Value.Reply));
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
