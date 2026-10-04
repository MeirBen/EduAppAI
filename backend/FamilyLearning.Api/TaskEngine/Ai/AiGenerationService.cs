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
    private readonly bool promptSchema = !options.Value.StrictSchema || options.Value.SchemaInPrompt;
    private readonly int exactQuestionCountLimit = options.Value.StrictSchema ? options.Value.StrictQuestionCountLimit : int.MaxValue;
    private readonly SemaphoreSlim capacity = new(2, 2);
    private static readonly JsonSerializerOptions Json = EngineJson.Options;

    public bool Configured => client is not null;

    /// <summary>Interprets one bounded parent message. Normalization and computed changes never authorize publication.</summary>
    public async Task<AiResult<AuthoringReply>> AuthorAsync(TemplateAuthoringInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        ValidateAuthoring(input);
        var request = JsonSerializer.Serialize(new { input.Message, input.BaseDefinition, context = input.Context ?? [] }, Json);
        const string stage = "author";
        var result = await RequestAsync<AuthoringCandidate>(AiPrompts.PlanAuthoring, request, AiSchemas.Template, AiPrompts.Version(stage), ct, evidence: evidence);
        var reply = result.Value.Result;
        var assumptions = result.Value.Assumptions;
        if (reply is null || (reply.Proposal is null) == (reply.Clarification is null) ||
            reply.Clarification is not null && (string.IsNullOrWhiteSpace(reply.Clarification) || reply.Clarification.Length > 1000) ||
            assumptions is not { Length: <= 8 } || assumptions.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 200))
            throw InvalidOutput("authoring-envelope", AiPrompts.Version(stage));
        try
        {
            var plan = reply.Proposal is null ? null : PlanChanges.AssignNewIds(reply.Proposal, input.BaseDefinition);
            return new(new(plan, reply.Clarification, assumptions,
                plan is null ? [] : PlanChanges.Compare(input.BaseDefinition, plan)), result.Metadata);
        }
        catch (TaskValidationException exception) { throw InvalidOutput("plan-validation", AiPrompts.Version(stage), exception.Errors); }
    }

    /// <summary>One generated-material batch call. Strict rejection returns diagnostics and never applies partial material.</summary>
    public async Task<AiResult<MaterialCandidateBatch>> GenerateMaterialsAsync(MaterialGenerationInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var current = new TaskDocument("", null, input.Materials, []);
        var prepared = TaskAssembly.PrepareMaterials(input.Request, current)
            ?? throw new TaskValidationException("materials", "אין חומרים חסרים או מיושנים ליצירה.");
        current = current with { Materials = prepared.Materials };
        var ids = input.Request.Materials.Where(m => m.Source == "generated").Select(m => m.Id).ToArray();
        // The app owns the randomness: the model lists several premises and writes the one drawn here.
        var request = JsonSerializer.SerializeToNode(EffectiveInput(input.Request), Json)!.AsObject();
        request["variation"] = Random.Shared.Next(1, AiPrompts.Variations + 1);
        var result = await RequestAsync<MaterialCandidateBatch>(AiPrompts.MaterialGeneration,
            request.ToJsonString(Json), AiSchemas.MaterialsFor(ids), AiPrompts.Version("materials"), ct, evidence: evidence);
        var accepted = TaskAssembly.AcceptMaterials(input.Request, current, result.Value, result.Metadata);
        if (accepted.Document is null) throw InvalidOutput("material-validation", result.Metadata.PromptVersion, accepted.Diagnostics);
        return result;
    }

    /// <summary>One full question call against current strict-valid material. Source revisions come from the application.</summary>
    public async Task<AiResult<QuestionCandidateBatch>> GenerateQuestionsAsync(QuestionGenerationInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var current = new TaskDocument("", null, input.Materials, []);
        var prepared = TaskAssembly.PrepareQuestions(input.Request, current);
        var result = await RequestAsync<QuestionCandidateBatch>(AiPrompts.QuestionGeneration,
            JsonSerializer.Serialize(new { request = EffectiveInput(input.Request), materials = SourceContext(prepared.Materials) }, Json),
            AiSchemas.QuestionsFor(input.Request, exactQuestionCountLimit), AiPrompts.Version("questions"), ct, evidence: evidence);
        var errors = TaskDocumentValidator.ValidateQuestionBatch(input.Request, current with { Materials = prepared.Materials }, result.Value);
        if (errors.Count > 0) throw InvalidOutput("question-validation", result.Metadata.PromptVersion, errors);
        return result;
    }

    /// <summary>One complete generated-material replacement; source authority and target safety are enforced before/after the call.</summary>
    public async Task<AiResult<MaterialCandidate>> ReplaceMaterialAsync(MaterialReplacementInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var target = TaskAssembly.MaterialTarget(input);
        var result = await RequestAsync<MaterialCandidate>(AiPrompts.MaterialReplacement,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                target = new { target.Id, target.Title, target.Body },
                materials = SourceContext(input.Current.Materials.Where(m => m.Id != target.Id)),
                input.Instruction
            }, Json), AiSchemas.MaterialsFor([target.Id], replacement: true), AiPrompts.Version("replace-material"), ct, evidence: evidence);
        try { TaskAssembly.ReplaceMaterial(input, result.Value, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("material-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
    }

    /// <summary>One complete question replacement; the rest of the activity is read-only context and app-owned identities never enter the provider request.</summary>
    public async Task<AiResult<QuestionCandidate>> ReplaceQuestionAsync(QuestionReplacementInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var target = TaskAssembly.QuestionTarget(input);
        var result = await RequestAsync<QuestionCandidate>(AiPrompts.QuestionReplacement,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                target = QuestionContext(target),
                otherQuestions = input.Current.Questions.Where(q => q.Id != target.Id).Select(QuestionContext).ToArray(),
                learnerInstructions = input.Current.Instructions,
                materials = SourceContext(input.Current.Materials),
                input.Instruction
            }, Json), AiSchemas.QuestionsFor(input.Request, exactQuestionCountLimit, replacement: true), AiPrompts.Version("replace-question"), ct, evidence: evidence);
        try { TaskAssembly.ReplaceQuestion(input, result.Value, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("question-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
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

    private static object QuestionContext(DocumentQuestion question) =>
        new { question.Prompt, question.Interaction, question.Answer, question.Points };

    private static void ValidateAuthoring(TemplateAuthoringInput input)
    {
        var errors = input.BaseDefinition is null ? new Dictionary<string, string[]>() : LearningPlanValidator.Validate(input.BaseDefinition);
        if (string.IsNullOrWhiteSpace(input.Message) || input.Message.Length > EngineValidation.MessageLength)
            errors["message"] = [$"יש לכתוב בקשה באורך של עד {EngineValidation.Count(EngineValidation.MessageLength)} תווים."];
        if (input.Context is { } context && (context.Length > EngineValidation.MaxContextTurns || context.Any(t => t is null ||
            t.Role is not ("parent" or "assistant") || string.IsNullOrWhiteSpace(t.Text)) || context.Sum(t => (long)t.Text.Length) > EngineValidation.ContextLength))
            errors["context"] = [$"יש לאחד את הבקשה לפני שממשיכים: עד {EngineValidation.MaxContextTurns} תורים ו־{EngineValidation.Count(EngineValidation.ContextLength)} תווים."];
        if (errors.Count > 0) throw new TaskValidationException(errors);
    }

    private async Task<AiResult<T>> RequestAsync<T>(string systemPrompt, string input, JsonElement schema,
        string promptVersion, CancellationToken ct, AiCallEvidence? evidence = null) where T : class
    {
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > maxSchemaBytes) throw AiGenerationException.InputLimit("schema-limit");
        if (client is null) throw new AiGenerationException(503, "יצירת תוכן בעזרת AI עדיין לא מחוברת. יש להגדיר מפתח OpenRouter בשרת.");
        if (!await capacity.WaitAsync(0, ct)) throw new AiGenerationException(503, "שירות היצירה עסוק כרגע. אפשר לנסות שוב בעוד רגע.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(requestTimeout);
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Endpoints that show the native schema need no copy, which can lower output quality; other modes rely on it.
            var instructions = promptSchema ? $"{systemPrompt}\nOutput JSON schema:\n{JsonSerializer.Serialize(schema, Json)}" : systemPrompt;
            if (evidence is not null)
            {
                evidence.Request = instructions + "\n" + input;
                evidence.Schema = schema.Clone();
            }
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, input)],
                new ChatOptions
                {
                    ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, promptVersion.Replace('-', '_')),
                    MaxOutputTokens = maxOutputTokens,
                    AdditionalProperties = new() { ["strict"] = true }
                }, timeout.Token);
            var text = response.Text;
            var metadata = new GenerationMetadata("OpenRouter", response.ModelId is { Length: <= AiCallEvidence.IdentifierLimit } model ? model : "unknown", promptVersion, DateTime.UtcNow,
                EngineVersions.Revision, EngineVersions.SchemaVersion);
            evidence?.Capture(response, metadata);
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
