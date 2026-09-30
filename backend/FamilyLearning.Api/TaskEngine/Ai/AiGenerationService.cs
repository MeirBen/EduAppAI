using System.ClientModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>AI template authoring and task generation through one provider boundary.</summary>
/// <remarks>Singleton; the semaphore caps in-flight provider calls. This service has no persistence or identity access.</remarks>
public sealed class AiGenerationService(IEnumerable<IChatClient> clients, ILogger<AiGenerationService> logger,
    IOptions<AiGenerationOptions> options) : IDisposable
{
    private readonly IChatClient? client = clients.SingleOrDefault();
    private readonly TimeSpan requestTimeout = options.Value.RequestTimeout;
    private readonly int maxOutputTokens = options.Value.MaxOutputTokens;
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
        var errors = TemplateValidator.Validate(result.Value);
        if (errors.Count > 0)
            throw InvalidOutput("template-validation", AiPrompts.AuthoringVersion, errors);
        return result;
    }

    /// <summary>Generates any supported subject from a validated blueprint and resolved task input.</summary>
    public async Task<AiResult<TaskContent>> GenerateAsync(TaskTemplateDefinition definition, TaskInput input, CancellationToken ct)
    {
        // Send chosen settings only; template defaults belong to authoring and must not compete with them.
        var request = JsonSerializer.Serialize(new
        {
            instructions = definition.Generation.Instructions,
            input.Settings,
            parameterDefinitions = definition.InstanceParameters,
            input.Parameters
        }, Json);
        var result = await RequestAsync<TaskContent>(AiPrompts.Instance, request, AiSchemas.Content, AiPrompts.InstanceVersion, ct);
        var errors = TaskContentValidator.Validate(result.Value, input.Settings.QuestionCount);
        if (errors.Count > 0)
            throw InvalidOutput("task-validation", AiPrompts.InstanceVersion, errors);
        return result;
    }

    private async Task<AiResult<T>> RequestAsync<T>(string systemPrompt, string input, JsonElement schema,
        string promptVersion, CancellationToken ct) where T : class
    {
        if (client is null) throw new AiGenerationException(503, "יצירת תוכן בעזרת AI עדיין לא מחוברת. יש להגדיר מפתח OpenRouter בשרת.");
        if (!await capacity.WaitAsync(0, ct)) throw new AiGenerationException(503, "שירות היצירה עסוק כרגע. אפשר לנסות שוב בעוד רגע.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(requestTimeout);
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Keep the model-visible contract and output constraint sourced from the same schema.
            var instructions = $"{systemPrompt}\nOutput JSON schema:\n{JsonSerializer.Serialize(schema, Json)}";
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, input)],
                new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, promptVersion.Replace('-', '_')),
                    MaxOutputTokens = maxOutputTokens,
                    AdditionalProperties = new() { ["strict"] = true }
                }, timeout.Token);
            var text = response.Text;
            var metadata = new GenerationMetadata("OpenRouter", response.ModelId ?? "unknown", promptVersion, DateTime.UtcNow);
            // Record metadata before parsing so truncated and invalid responses remain diagnosable.
            logger.LogInformation(
                "AI response: provider {Provider}, model {Model}, prompt version {PromptVersion}, generated {GeneratedAtUtc}, " +
                "finish {FinishReason}, characters {CharacterCount}, elapsed {ElapsedMilliseconds} ms, " +
                "input tokens {InputTokens}, output tokens {OutputTokens}, reasoning tokens {ReasoningTokens}",
                metadata.Provider, metadata.Model, metadata.PromptVersion, metadata.GeneratedAtUtc,
                response.FinishReason, text.Length, Stopwatch.GetElapsedTime(started).TotalMilliseconds, response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount, response.Usage?.ReasoningTokenCount);
            if (response.FinishReason != ChatFinishReason.Stop)
                throw InvalidOutput(response.FinishReason == ChatFinishReason.Length ? "output-limit" : "incomplete-response", promptVersion);
            if (text.Length is 0 or > 32000)
                throw InvalidOutput("response-size", promptVersion);
            var value = JsonSerializer.Deserialize<T>(text, Json) ?? throw InvalidOutput("null-json", promptVersion);
            return new(value, metadata);
        }
        catch (JsonException) { throw InvalidOutput("json-contract", promptVersion); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AI request timed out: prompt version {PromptVersion}, elapsed {ElapsedMilliseconds} ms",
                promptVersion, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw new AiGenerationException(504, "יצירת התוכן ארכה יותר מדי זמן. לא נשמר דבר. אפשר לנסות שוב.");
        }
        catch (Exception exception) when (exception is HttpRequestException or ClientResultException)
        {
            // Provider exceptions may contain request content or credentials. Do not log their bodies.
            var status = exception switch
            {
                ClientResultException response => response.Status,
                HttpRequestException request => (int?)request.StatusCode,
                _ => null
            };
            logger.LogWarning("AI provider request failed ({ExceptionType}, HTTP {StatusCode}): prompt version {PromptVersion}, elapsed {ElapsedMilliseconds} ms",
                exception.GetType().Name, status, promptVersion, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (status == 429)
                throw new AiGenerationException(429, "שירות ה־AI הגיע למגבלת הבקשות. לא נשמר דבר. יש לנסות שוב מאוחר יותר.");
            throw new AiGenerationException(502, "שירות ה־AI לא הצליח ליצור תוכן כרגע. לא נשמר דבר. אפשר לנסות שוב.");
        }
        finally { capacity.Release(); }
    }

    private AiGenerationException InvalidOutput(string failure, string promptVersion,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        logger.LogWarning("AI output rejected: {Failure}, prompt version {PromptVersion}", failure, promptVersion);
        return failure == "output-limit" ? AiGenerationException.OutputLimit() : AiGenerationException.InvalidOutput(errors);
    }

    public void Dispose() => capacity.Dispose();
}
