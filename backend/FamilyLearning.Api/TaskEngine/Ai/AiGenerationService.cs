using System.ClientModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>AI template authoring and task generation through one provider boundary.</summary>
/// <remarks>Singleton; the semaphore caps in-flight provider calls. This service has no persistence or identity access.</remarks>
public sealed class AiGenerationService(IEnumerable<IChatClient> clients, ILogger<AiGenerationService> logger) : IDisposable
{
    private readonly IChatClient? client = clients.SingleOrDefault();
    private readonly SemaphoreSlim capacity = new(2, 2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 16,
        // These messages are sent as JSON to the provider, never inserted into HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public bool Configured => client is not null;

    /// <summary>Produces a validated, unsaved AI blueprint for explicit parent review and publication.</summary>
    public async Task<AiResult<TaskTemplateDefinition>> AuthorAsync(string prompt, CancellationToken ct)
    {
        var result = await RequestAsync<TaskTemplateDefinition>(AiPrompts.Authoring, prompt, AiSchemas.Template,
            AiPrompts.AuthoringVersion, ct);
        if (TemplateValidator.Validate(result.Value).Count > 0)
            throw AiGenerationException.InvalidOutput();
        return result;
    }

    /// <summary>Generates any supported subject from a validated blueprint and already resolved parameters.</summary>
    public async Task<AiResult<TaskContent>> GenerateAsync(TaskTemplateDefinition definition,
        Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        int? expectedCount = definition.Generation.QuestionCountParameter is { } key ? parameters[key].GetInt32() : null;
        var input = JsonSerializer.Serialize(new { definition, parameters, expectedQuestionCount = expectedCount }, Json);
        var result = await RequestAsync<TaskContent>(AiPrompts.Instance, input, AiSchemas.Content, AiPrompts.InstanceVersion, ct);
        if (TaskContentValidator.Validate(result.Value).Count > 0 ||
            (expectedCount.HasValue && result.Value.Questions.Length != expectedCount.Value))
            throw AiGenerationException.InvalidOutput();
        return result;
    }

    private async Task<AiResult<T>> RequestAsync<T>(string systemPrompt, string input, JsonElement schema,
        string promptVersion, CancellationToken ct) where T : class
    {
        if (client is null) throw new AiGenerationException(503, "יצירת תוכן בעזרת AI עדיין לא מחוברת. יש להגדיר מפתח OpenRouter בשרת.");
        if (!await capacity.WaitAsync(0, ct)) throw new AiGenerationException(503, "שירות היצירה עסוק כרגע. אפשר לנסות שוב בעוד רגע.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, input)],
                new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, promptVersion.Replace('-', '_')),
                    MaxOutputTokens = 8192,
                    AdditionalProperties = new() { ["strict"] = true }
                }, timeout.Token);
            if (response.FinishReason != ChatFinishReason.Stop || response.Text.Length is 0 or > 32000)
                throw AiGenerationException.InvalidOutput();
            var value = JsonSerializer.Deserialize<T>(response.Text, Json) ?? throw AiGenerationException.InvalidOutput();
            var metadata = new GenerationMetadata("OpenRouter", response.ModelId ?? "unknown", promptVersion, DateTime.UtcNow);
            logger.LogInformation("AI response: provider {Provider}, model {Model}, prompt version {PromptVersion}, generated {GeneratedAtUtc}",
                metadata.Provider, metadata.Model, metadata.PromptVersion, metadata.GeneratedAtUtc);
            return new(value, metadata);
        }
        catch (JsonException) { throw AiGenerationException.InvalidOutput(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new AiGenerationException(504, "יצירת התוכן ארכה יותר מדי זמן. לא נשמר דבר. אפשר לנסות שוב."); }
        catch (Exception exception) when (exception is HttpRequestException or ClientResultException)
        {
            // Provider exceptions may contain request content or credentials. Do not log their bodies.
            logger.LogWarning("AI provider request failed ({ExceptionType})", exception.GetType().Name);
            throw new AiGenerationException(502, "שירות ה־AI לא הצליח ליצור תוכן כרגע. לא נשמר דבר. אפשר לנסות שוב.");
        }
        finally { capacity.Release(); }
    }

    public void Dispose() => capacity.Dispose();
}
