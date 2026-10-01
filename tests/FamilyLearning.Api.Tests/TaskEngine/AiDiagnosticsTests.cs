using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class AiDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_output_logs_model_and_finish_reason_without_learning_content(bool truncated)
    {
        using var chat = new AiFixtures.ScriptedChat("private generated answer")
        {
            FinishReason = truncated ? ChatFinishReason.Length : ChatFinishReason.Stop
        };
        var logger = new CaptureLogger();
        using var service = new AiGenerationService([chat], logger, Options.Create(new AiGenerationOptions()));

        await Assert.ThrowsAsync<AiGenerationException>(() => service.AuthorAsync(new FamilyLearning.Api.TaskEngine.Models.TemplateAuthoringInput("private parent prompt"), CancellationToken.None));

        var response = Assert.Single(logger.Entries, entry => entry.ContainsKey("FinishReason"));
        Assert.Equal("test-free-model", response["Model"]);
        Assert.Equal(truncated ? ChatFinishReason.Length : ChatFinishReason.Stop, response["FinishReason"]);
        Assert.True(response.ContainsKey("ReasoningTokens"));
        Assert.True(response.ContainsKey("ElapsedMilliseconds"));
        Assert.Contains(logger.Entries, entry => entry.ContainsKey("Failure"));
        var logs = string.Join(" ", logger.Entries.SelectMany(entry => entry.Values));
        Assert.DoesNotContain("private generated answer", logs);
        Assert.DoesNotContain("private parent prompt", logs);
    }

    private sealed class CaptureLogger : ILogger<AiGenerationService>
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
    }
}
