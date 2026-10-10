using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.TaskEngine.Ai;

/// <summary>Activity authoring, revision and content generation through one provider boundary.</summary>
/// <remarks>Singleton without persistence or identity access; callers hold an <see cref="AiCapacity"/> slot for each call.</remarks>
public sealed class AiGenerationService(IEnumerable<IChatClient> clients, ILogger<AiGenerationService> logger,
    IOptions<AiGenerationOptions> options)
{
    private readonly IChatClient? client = clients.SingleOrDefault();
    private readonly TimeSpan requestTimeout = options.Value.RequestTimeout;
    private readonly int maxOutputTokens = options.Value.MaxOutputTokens;
    private readonly int maxSchemaBytes = options.Value.MaxSchemaBytes;
    private readonly int maxRequestBytes = options.Value.MaxRequestBytes;
    private readonly bool promptSchema = !options.Value.StrictSchema || options.Value.SchemaInPrompt;
    private readonly int exactQuestionCountLimit = options.Value.StrictSchema ? options.Value.StrictQuestionCountLimit : int.MaxValue;
    private static readonly JsonSerializerOptions Json = EngineJson.Options;

    public bool Configured => client is not null;

    public async Task<AiResult<RevisionDecision>> ReviseAsync(ActivityRevisionInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        ActivityRevisionValidator.ValidateInput(input);
        var payload = new
        {
            input.Plan,
            document = new
            {
                input.Current.Title,
                input.Current.Instructions,
                materials = input.Current.Materials.Select(m => new { m.Id, m.Title, body = input.Plan.Materials.Any(r => r.Id == m.Id && r.Source == "supplied") ? null : m.Body }),
                questions = input.Current.Questions.Select(q => new { q.Id, q.Prompt, q.Interaction, q.Answer, q.Points })
            },
            input.Message,
            input.Target,
            context = input.Context ?? []
        };
        var result = await RequestAsync<RevisionCandidate>(AiPrompts.ActivityRevision, JsonSerializer.Serialize(payload, Json),
            AiSchemas.RevisionFor(input), AiPrompts.Version("revise"), ct, evidence);
        try { return new(ActivityRevisionValidator.Validate(result.Value.Result, input), result.Metadata); }
        catch (TaskValidationException error) { throw InvalidOutput("revision-validation", result.Metadata.PromptVersion, error.Errors); }
    }

    /// <summary>Interprets one bounded parent message. Normalization and computed changes never authorize publication.</summary>
    public async Task<AiResult<AuthoringReply>> AuthorAsync(ActivityAuthoringInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        ValidateAuthoring(input);
        var context = ConversationWindow.Latest(input.Context ?? [], turn => turn.Text);
        var request = JsonSerializer.Serialize(new { input.Message, input.BaseDefinition, context }, Json);
        const string stage = "author";
        var result = await RequestAsync<AuthoringCandidate>(AiPrompts.PlanAuthoring, request, AiSchemas.Authoring, AiPrompts.Version(stage), ct, evidence: evidence);
        var reply = result.Value.Result;
        var assumptions = result.Value.Assumptions;
        if (reply is null || (reply.Proposal is null) == (reply.Clarification is null) ||
            reply.Clarification is not null && !EngineValidation.HasText(reply.Clarification, EngineValidation.AuthoringReplyLength) ||
            assumptions is not { Length: <= EngineValidation.MaxAssumptions } || assumptions.Any(a => !EngineValidation.HasText(a, EngineValidation.AssumptionLength)))
            throw InvalidOutput("authoring-envelope", AiPrompts.Version(stage));
        try
        {
            var plan = reply.Proposal is null ? null : PlanChanges.AssignNewIds(reply.Proposal, input.BaseDefinition);
            var changes = plan is null ? [] : PlanChanges.Compare(input.BaseDefinition, plan);
            return new(new(plan, reply.Clarification ?? ProposalReply(input.BaseDefinition, changes), assumptions, changes), result.Metadata);
        }
        catch (TaskValidationException exception) { throw InvalidOutput("plan-validation", AiPrompts.Version(stage), exception.Errors); }
    }

    /// <summary>Proposes <see cref="MaterialIdeas.CandidateCount"/> ideas scored against <paramref name="history"/>; the caller selects one.</summary>
    public async Task<AiResult<MaterialIdeaCandidateBatch>> GenerateMaterialIdeasAsync(MaterialGenerationInput input, MaterialIdea[] history,
        CancellationToken ct, AiCallEvidence? evidence = null)
    {
        RequireMaterials(input);
        var result = await RequestAsync<MaterialIdeaCandidateBatch>(AiPrompts.MaterialIdeaGeneration,
            JsonSerializer.Serialize(new { request = EffectiveInput(input.Request), targetIds = MaterialTargets(input), materials = SourceContext(input.Materials), history }, Json),
            AiSchemas.Ideas, AiPrompts.Version("material-ideas"), ct, evidence: evidence);
        try { MaterialIdeas.Validate(result.Value); }
        catch (TaskValidationException exception) { throw InvalidOutput("idea-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
    }

    /// <summary>Writes the generated-material batch from the selected <paramref name="idea"/>. Strict rejection never applies partial material.</summary>
    public async Task<AiResult<MaterialCandidateBatch>> GenerateMaterialsAsync(MaterialGenerationInput input, MaterialIdea idea,
        CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var current = new TaskDocument("", null, RequireMaterials(input).Materials, []);
        var ids = MaterialTargets(input);
        var result = await RequestAsync<MaterialCandidateBatch>(AiPrompts.MaterialGeneration,
            JsonSerializer.Serialize(new { request = EffectiveInput(input.Request), targetIds = ids, materials = SourceContext(input.Materials), idea }, Json),
            AiSchemas.MaterialsFor(ids), AiPrompts.Version("materials"), ct, evidence: evidence);
        var accepted = TaskAssembly.AcceptMaterials(input.Request, current, result.Value, result.Metadata, idea, input.TargetIds);
        if (accepted.Document is null) throw InvalidOutput("material-validation", result.Metadata.PromptVersion, accepted.Diagnostics);
        return result;
    }

    /// <summary>One full question call against current strict-valid material, varied from <paramref name="history"/> prompts.
    /// Source revisions come from the application.</summary>
    public async Task<AiResult<QuestionCandidateBatch>> GenerateQuestionsAsync(QuestionGenerationInput input, string[] history,
        CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var current = new TaskDocument("", null, input.Materials, []);
        var prepared = TaskAssembly.PrepareQuestions(input.Request, current);
        var result = await RequestAsync<QuestionCandidateBatch>(AiPrompts.QuestionGeneration,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                input.Request.DocumentGuidance,
                materials = SourceContext(prepared.Materials),
                history,
                previous = input.Current is null ? null : QuestionReference(input.Current),
                input.Instruction
            }, Json),
            AiSchemas.QuestionsFor(input.Request, exactQuestionCountLimit), AiPrompts.Version("questions"), ct, evidence: evidence);
        var batch = result.Value.Trimmed();
        var errors = TaskDocumentValidator.ValidateQuestionBatch(input.Request, current with { Materials = prepared.Materials }, batch);
        if (errors.Count > 0) throw InvalidOutput("question-validation", result.Metadata.PromptVersion, errors);
        return result with { Value = batch };
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
                materials = input.Current.Materials.Where(m => m.Id != target.Id).Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Body,
                    state = input.PendingIds?.Contains(m.Id) == true ? "pending" : "final"
                }),
                input.Instruction
            }, Json), AiSchemas.MaterialsFor([target.Id], replacement: true), AiPrompts.Version("replace-material"), ct, evidence: evidence);
        try { TaskAssembly.ReplaceMaterial(input, result.Value, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("material-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
    }

    /// <summary>One complete question replacement; the rest of the activity is read-only context and question identities never enter the provider request.</summary>
    public async Task<AiResult<QuestionCandidate>> ReplaceQuestionAsync(QuestionReplacementInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var target = TaskAssembly.QuestionTarget(input);
        var result = await RequestAsync<QuestionCandidate>(AiPrompts.QuestionReplacement,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                target = QuestionContext(target),
                otherQuestions = input.Current.Questions.Where(q => q.Id != target.Id).Select(q => new { q.Prompt, q.Interaction, q.Points }).ToArray(),
                learnerInstructions = input.Current.Instructions,
                materials = SourceContext(input.Current.Materials),
                input.Instruction
            }, Json), AiSchemas.QuestionsFor(input.Request, exactQuestionCountLimit, replacement: true), AiPrompts.Version("replace-question"), ct, evidence: evidence);
        var candidate = result.Value.Trimmed();
        try { TaskAssembly.ReplaceQuestion(input, candidate, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("question-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result with { Value = candidate };
    }

    /// <summary>One minimal-edit pass over every accepted generated material, in the writing schema; supplied sources are context only.</summary>
    public async Task<AiResult<MaterialCandidateBatch>> PolishMaterialsAsync(PolishInput input, CancellationToken ct, AiCallEvidence? evidence = null)
    {
        var targets = TaskAssembly.MaterialPolishTargets(input);
        var result = await RequestAsync<MaterialCandidateBatch>(AiPrompts.MaterialPolish,
            JsonSerializer.Serialize(new
            {
                request = EffectiveInput(input.Request),
                materials = targets.Select(m => new { m.Id, m.Title, m.Body }),
                retained = SourceContext(input.Current.Materials.Where(m => !targets.Any(t => t.Id == m.Id)))
            }, Json),
            AiSchemas.MaterialsFor(targets.Select(m => m.Id).ToArray()), AiPrompts.Version("material-polish"), ct, evidence: evidence);
        try { TaskAssembly.PolishMaterials(input, result.Value, result.Metadata); }
        catch (TaskValidationException exception) { throw InvalidOutput("material-validation", result.Metadata.PromptVersion, exception.Errors); }
        return result;
    }

    public async Task<AiResult<QuestionAdditionBatch>> AppendQuestionsAsync(QuestionAdditionInput input, string[] history,
        CancellationToken ct, AiCallEvidence? evidence = null)
    {
        TaskAssembly.PrepareQuestions(input.Request, input.Current);
        if (input.Count <= 0 || input.Count + input.Current.Questions.Length != input.Request.Settings.QuestionCount)
            throw new TaskValidationException("questions", "מספר התוספות אינו תואם לדרישה.");
        var result = await RequestAsync<QuestionAdditionBatch>(AiPrompts.QuestionAddition, JsonSerializer.Serialize(new
        {
            request = EffectiveInput(input.Request),
            materials = SourceContext(input.Current.Materials),
            existing = QuestionReference(input.Current),
            additionalCount = input.Count,
            input.Instruction,
            history
        }, Json), AiSchemas.QuestionsFor(input.Request, exactQuestionCountLimit, outputCount: input.Count), AiPrompts.Version("append-questions"), ct, evidence);
        var candidate = result.Value with { Questions = result.Value.Questions?.Select(q => q?.Trimmed()!).ToArray()! };
        try { TaskAssembly.AppendQuestions(input, candidate, result.Metadata); }
        catch (TaskValidationException error) { throw InvalidOutput("question-validation", result.Metadata.PromptVersion, error.Errors); }
        return result with { Value = candidate };
    }

    // Version/provenance fields are evidence for the caller, never competing generation requirements.
    private static object EffectiveInput(ResolvedTaskRequest request) => new
    {
        request.Goal,
        request.Guidance,
        request.Settings,
        materials = request.Materials.Select(m => new { m.Id, m.Label, m.Source, m.Guidance, m.Length }),
        request.Questions,
        request.TotalLength
    };

    private static MaterialGenerationInput RequireMaterials(MaterialGenerationInput input)
    {
        if (input.TargetIds is null) return TaskAssembly.RequireMaterialWork(input.Request, new("", null, input.Materials, []));
        if (input.TargetIds.Length == 0 || input.TargetIds.Distinct(StringComparer.Ordinal).Count() != input.TargetIds.Length ||
            input.TargetIds.Any(id => !input.Request.Materials.Any(m => m.Id == id && m.Source == "generated")))
            throw new TaskValidationException("materials", "יש לבחור טקסטים חדשים ליצירה.");
        var errors = TaskDocumentValidator.ValidateDraft(input.Request, new("", null, input.Materials, [])).Errors;
        if (errors.Count > 0) throw new TaskValidationException(errors);
        return input;
    }

    private static string[] MaterialTargets(MaterialGenerationInput input) => input.TargetIds ??
        input.Request.Materials.Where(m => m.Source == "generated").Select(m => m.Id).ToArray();

    private static object SourceContext(IEnumerable<MaterialContent> materials) =>
        materials.Select(m => new { m.Id, m.Title, m.Body }).ToArray();

    private static object QuestionReference(TaskDocument current) => new
    {
        current.Title,
        current.Instructions,
        questions = current.Questions.Select(q => new { q.Id, q.Prompt, q.Interaction, q.Points })
    };

    /// <summary>The assistant's turn beside a proposal; the client shows it and sends it back as conversation.</summary>
    private static string ProposalReply(LearningPlan? basis, PlanChange[] changes) =>
        changes.Length == 0 ? "ההגדרות כבר תואמות לבקשה." :
        basis is null ? "הכנו הגדרות לפי הבקשה. בדקו אותן וצרו את הפעילות." : "ההגדרות עודכנו. אפשר לבקש שינוי או לבטל אותו.";

    private static object QuestionContext(DocumentQuestion question) =>
        new { question.Prompt, question.Interaction, question.Answer, question.Points };

    private static void ValidateAuthoring(ActivityAuthoringInput input)
    {
        var errors = input.BaseDefinition is null ? new Dictionary<string, string[]>() : LearningPlanValidator.Validate(input.BaseDefinition);
        if (string.IsNullOrWhiteSpace(input.Message) || input.Message.Length > EngineValidation.MessageLength)
            errors["message"] = [$"יש לכתוב בקשה באורך של עד {EngineValidation.Count(EngineValidation.MessageLength)} תווים."];
        // The same bounds as an imported chat, so any conversation planning accepts can become the draft's chat.
        if (input.Context is { } context && (context.Length > EngineValidation.MaxChatTurns ||
            context.Any(t => t is null || !EngineValidation.IsTurn(t.Role, t.Text))))
            errors["context"] = ["השיחה אינה תקינה או ארוכה מדי."];
        if (errors.Count > 0) throw new TaskValidationException(errors);
    }

    private async Task<AiResult<T>> RequestAsync<T>(string systemPrompt, string input, JsonElement schema,
        string promptVersion, CancellationToken ct, AiCallEvidence? evidence = null) where T : class
    {
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > maxSchemaBytes) throw AiGenerationException.InputLimit("schema-limit");
        // The transport also measures its exact HTTP envelope. Reject required context before any provider implementation runs.
        if (RequestSize() > maxRequestBytes)
        {
            var payload = JsonNode.Parse(input)!;
            foreach (var property in new[] { "context", "history" })
                if (payload[property] is JsonArray history)
                    while (history.Count > 0 && RequestSize() > maxRequestBytes)
                    {
                        // Conversation is oldest-first; novelty history is newest-first.
                        history.RemoveAt(property == "context" ? 0 : history.Count - 1);
                        input = payload.ToJsonString(Json);
                    }
            if (RequestSize() > maxRequestBytes) throw AiGenerationException.InputLimit("request-limit");
        }
        int RequestSize() => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { systemPrompt, input, schema, promptSchema = promptSchema ? schema.GetRawText() : null }, Json));
        if (client is null) throw new AiGenerationException(503, "יצירת תוכן בעזרת AI עדיין לא מחוברת. יש להגדיר מפתח OpenRouter בשרת.");
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
            var usage = AiCallUsage.FromResponse(response);
            evidence?.Capture(response, metadata, usage);
            // Record metadata before parsing so truncated and invalid responses remain diagnosable.
            logger.LogInformation(
                "AI response: provider {Provider}, model {Model}, prompt version {PromptVersion}, generated {GeneratedAtUtc}, " +
                "finish {FinishReason}, characters {CharacterCount}, elapsed {ElapsedMilliseconds} ms, " +
                "input tokens {InputTokens}, output tokens {OutputTokens}, reasoning tokens {ReasoningTokens}, " +
                "cache read tokens {CacheReadTokens}, cache write tokens {CacheWriteTokens}, cost credits {CostCredits}",
                metadata.Provider, metadata.Model, metadata.PromptVersion, metadata.GeneratedAtUtc,
                response.FinishReason, text.Length, Stopwatch.GetElapsedTime(started).TotalMilliseconds, usage.InputTokens,
                usage.OutputTokens, usage.ReasoningTokens, usage.CacheReadTokens, usage.CacheWriteTokens, usage.CostCredits);
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
    }

    private AiGenerationException InvalidOutput(string failure, string promptVersion,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        logger.LogWarning("AI output rejected: {Failure}, prompt version {PromptVersion}", failure, promptVersion);
        return failure == "output-limit" ? AiGenerationException.OutputLimit() : AiGenerationException.InvalidOutput(errors);
    }
}
