using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    private readonly int maxSchemaBytes = options.Value.MaxSchemaBytes;
    private readonly SemaphoreSlim capacity = new(2, 2);
    private static readonly JsonSerializerOptions Json = EngineJson.Options;

    public bool Configured => client is not null;

    /// <summary>Produces a validated, unsaved AI blueprint for explicit parent review and publication.</summary>
    public async Task<AiResult<TaskTemplateDefinition>> AuthorAsync(string prompt, CancellationToken ct)
    {
        var result = await RequestAsync<TaskTemplateDefinition>(AiPrompts.Authoring, prompt, AiSchemas.Blueprint,
            AiPrompts.AuthoringVersion, ct);
        var errors = TemplateValidator.Validate(result.Value);
        if (errors.Count > 0)
            throw InvalidOutput("template-validation", AiPrompts.AuthoringVersion, errors);
        return result;
    }

    /// <summary>Interprets one bounded parent message. Normalization and computed changes never authorize publication.</summary>
    public async Task<AiResult<AuthoringReply>> AuthorAsync(TemplateAuthoringInput input, CancellationToken ct)
    {
        ValidateAuthoring(input);
        var request = JsonSerializer.Serialize(new { input.Message, input.BaseDefinition, context = input.Context ?? [] }, Json);
        const string stage = "author";
        var result = await RequestAsync<AuthoringCandidate>(AiPrompts.PlanAuthoring, request, AiSchemas.Template, AiPrompts.Version(stage), ct, structured: true);
        var reply = result.Value;
        if ((reply.Proposal is null) == (reply.Clarification is null) ||
            reply.Clarification is not null && (string.IsNullOrWhiteSpace(reply.Clarification) || reply.Clarification.Length > 1000) ||
            reply.Assumptions is not { Length: <= 8 } || reply.Assumptions.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 200))
            throw InvalidOutput("authoring-envelope", AiPrompts.Version(stage));
        try
        {
            var plan = reply.Proposal is null ? null : PlanChanges.AssignNewIds(reply.Proposal, input.BaseDefinition);
            return new(new(plan, reply.Clarification, reply.Assumptions,
                plan is null ? [] : PlanChanges.Compare(input.BaseDefinition, plan)), result.Metadata);
        }
        catch (TaskValidationException exception) { throw InvalidOutput("plan-validation", AiPrompts.Version(stage), exception.Errors); }
    }

    /// <summary>One generated-material batch call. Strict rejection returns diagnostics and never applies partial material.</summary>
    public async Task<AiResult<MaterialCandidateBatch>> GenerateMaterialsAsync(MaterialGenerationInput input, CancellationToken ct)
    {
        AiSchemas.RequireQuestionOutputCapacity(input.Request);
        var current = new TaskDocument("", null, input.Materials, []);
        var prepared = TaskAssembly.PrepareMaterials(input.Request, current)
            ?? throw new TaskValidationException(new Dictionary<string, string[]>() { ["materials"] = ["אין חומרים חסרים או מיושנים ליצירה."] });
        current = current with { Materials = prepared.Materials };
        var ids = input.Request.Materials.Where(m => m.Source == "generated").Select(m => m.Id).ToArray();
        var result = await RequestAsync<MaterialCandidateBatch>(AiPrompts.MaterialGeneration,
            JsonSerializer.Serialize(EffectiveInput(input.Request), Json), AiSchemas.MaterialsFor(ids), AiPrompts.Version("materials"), ct, structured: true);
        var accepted = TaskAssembly.AcceptMaterials(input.Request, current, result.Value, result.Metadata);
        if (accepted.Document is null) throw InvalidOutput("material-validation", result.Metadata.PromptVersion, accepted.Diagnostics);
        return result;
    }

    /// <summary>One full question call against current strict-valid material. Source revisions come from the application.</summary>
    public async Task<AiResult<QuestionCandidateBatch>> GenerateQuestionsAsync(QuestionGenerationInput input, CancellationToken ct)
    {
        var current = new TaskDocument("", null, input.Materials, []);
        var prepared = TaskAssembly.PrepareQuestions(input.Request, current);
        var result = await RequestAsync<QuestionCandidateBatch>(AiPrompts.QuestionGeneration,
            JsonSerializer.Serialize(new { request = EffectiveInput(input.Request), materials = SourceContext(prepared.Materials) }, Json),
            AiSchemas.QuestionsFor(input.Request), AiPrompts.Version("questions"), ct, structured: true);
        var errors = TaskDocumentValidator.ValidateQuestionBatch(input.Request, current with { Materials = prepared.Materials }, result.Value);
        if (errors.Count > 0) throw InvalidOutput("question-validation", result.Metadata.PromptVersion, errors);
        return result;
    }

    /// <summary>One complete generated-material replacement; source authority and target safety are enforced before/after the call.</summary>
    public async Task<AiResult<MaterialCandidate>> ReplaceMaterialAsync(MaterialReplacementInput input, CancellationToken ct)
    {
        var target = TaskAssembly.MaterialTarget(input);
        var result = await RequestAsync<MaterialCandidate>(AiPrompts.MaterialReplacement,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                target = new { target.Id, target.Title, target.Body },
                materials = SourceContext(input.Current.Materials.Where(m => m.Id != target.Id)),
                input.Instruction
            }, Json), AiSchemas.MaterialsFor([target.Id], replacement: true), AiPrompts.Version("replace-material"), ct, structured: true);
        try { TaskAssembly.ReplaceMaterial(input, result.Value, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("material-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
    }

    /// <summary>One complete question replacement; unrelated questions and app-owned target identity never enter the provider request.</summary>
    public async Task<AiResult<QuestionCandidate>> ReplaceQuestionAsync(QuestionReplacementInput input, CancellationToken ct)
    {
        var target = TaskAssembly.QuestionTarget(input);
        var result = await RequestAsync<QuestionCandidate>(AiPrompts.QuestionReplacement,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                target = new { target.Prompt, target.Interaction, target.Answer, target.Points },
                materials = SourceContext(input.Current.Materials),
                input.Instruction
            }, Json), AiSchemas.QuestionsFor(input.Request, replacement: true), AiPrompts.Version("replace-question"), ct, structured: true);
        try { TaskAssembly.ReplaceQuestion(input, result.Value, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("question-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
    }

    /// <summary>Temporary evaluator-only matched one-shot experiment; uses the same schemas, source assembly and strict checks.</summary>
    public async Task<AiResult<TaskDocument>> GenerateOneShotAsync(ResolvedTaskRequest request, CancellationToken ct)
    {
        var result = await RequestAsync<ActivityCandidate>(AiPrompts.OneShot, JsonSerializer.Serialize(EffectiveInput(request), Json),
            AiSchemas.OneShotFor(request), AiPrompts.Version("one-shot"), ct, structured: true);
        var current = TaskAssembly.CreateDocument(request);
        var materials = TaskAssembly.AcceptMaterials(request, current, new(result.Value.Materials), result.Metadata);
        if (materials.Document is null) throw InvalidOutput("material-validation", result.Metadata.PromptVersion, materials.Diagnostics);
        try
        {
            var document = TaskAssembly.AcceptQuestions(request, materials.Document,
                new(result.Value.Title, result.Value.Instructions, result.Value.Questions), result.Metadata);
            return new(document, result.Metadata);
        }
        catch (TaskValidationException exception) { throw InvalidOutput("question-validation", result.Metadata.PromptVersion, exception.Errors); }
    }

    // Version/provenance fields are evidence for the caller, never competing generation requirements.
    private static object EffectiveInput(ResolvedTaskRequest request) => new
    {
        request.Goal,
        request.Guidance,
        request.Settings,
        request.Materials,
        request.Questions,
        request.Controls,
        request.TotalLength
    };

    private static object SourceContext(IEnumerable<MaterialContent> materials) =>
        materials.Select(m => new { m.Id, m.Revision, m.Title, m.Body }).ToArray();

    private static void ValidateAuthoring(TemplateAuthoringInput input)
    {
        var errors = input.BaseDefinition is null ? new Dictionary<string, string[]>() : LearningPlanValidator.Validate(input.BaseDefinition);
        if (string.IsNullOrWhiteSpace(input.Message) || input.Message.Length > 4000) errors["message"] = ["יש לכתוב בקשה באורך של עד 4,000 תווים."];
        if (input.Context is { } context && (context.Length > 6 || context.Any(t => t is null ||
            t.Role is not ("parent" or "assistant") || string.IsNullOrWhiteSpace(t.Text)) || context.Sum(t => (long)t.Text.Length) > 12000))
            errors["context"] = ["יש לאחד את הבקשה לפני שממשיכים: עד שישה תורים ו־12,000 תווים."];
        if (errors.Count > 0) throw new TaskValidationException(errors);
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
        var result = await RequestAsync<TaskContent>(AiPrompts.Instance, request,
            AiSchemas.ContentFor(input.Settings.QuestionCount), AiPrompts.InstanceVersion, ct);
        var errors = TaskContentValidator.Validate(result.Value, input.Settings.QuestionCount);
        if (errors.Count > 0)
            throw InvalidOutput("task-validation", AiPrompts.InstanceVersion, errors);
        return result;
    }

    private async Task<AiResult<T>> RequestAsync<T>(string systemPrompt, string input, JsonElement schema,
        string promptVersion, CancellationToken ct, bool structured = false) where T : class
    {
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > maxSchemaBytes) throw AiGenerationException.InputLimit("schema-limit");
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
            var metadata = new GenerationMetadata("OpenRouter", response.ModelId ?? "unknown", promptVersion, DateTime.UtcNow,
                structured ? EngineVersions.Revision : null, structured ? EngineVersions.SchemaVersion : null);
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
            if (text.Length is 0 or > AiGenerationOptions.OutputCharacterLimit)
                throw InvalidOutput("response-size", promptVersion);
            var value = JsonSerializer.Deserialize<T>(text, Json) ?? throw InvalidOutput("null-json", promptVersion);
            return new(value, metadata);
        }
        catch (JsonException) { throw InvalidOutput("json-contract", promptVersion); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("AI request timed out: prompt version {PromptVersion}, elapsed {ElapsedMilliseconds} ms",
                promptVersion, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw new AiGenerationException(504, "יצירת התוכן ארכה יותר מדי זמן. אפשר לנסות שוב.") { Category = "timeout" };
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
                throw new AiGenerationException(429, "שירות ה־AI הגיע למגבלת הבקשות. יש לנסות שוב מאוחר יותר.") { Category = "rate-limit" };
            throw new AiGenerationException(502, "שירות ה־AI לא הצליח ליצור תוכן כרגע. אפשר לנסות שוב.");
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

internal sealed record ActivityCandidate(
    [property: System.Text.Json.Serialization.JsonRequired] MaterialCandidate[] Materials,
    [property: System.Text.Json.Serialization.JsonRequired] string Title, string? Instructions,
    [property: System.Text.Json.Serialization.JsonRequired] QuestionCandidate[] Questions);
