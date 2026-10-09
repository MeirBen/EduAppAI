using System.Security.Cryptography;
using System.Text.Json;
using FamilyLearning.Api.Features.Library;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>One process, one sequential content caller. SQLite checkpoints own recovery; no transaction crosses a provider call.</summary>
public sealed partial class GenerationWorker(IServiceScopeFactory scopes, AiGenerationService ai, TimeProvider clock,
    IOptions<GenerationOperationOptions> options, IOptions<AiGenerationOptions> aiOptions, IConfiguration configuration,
    LibraryChanges changes, ILogger<GenerationWorker> logger) : BackgroundService
{
    private readonly object transportLock = new();
    private Guid? callingId;
    private CancellationTokenSource? transport;
    internal string ProfileFingerprint { get; } = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        profile = AiProfile.Capture(configuration, aiOptions.Value),
        endpoint = configuration["Ai:Endpoint"] ?? "https://openrouter.ai/api/v1"
    })));
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await RecoverAsync(stoppingToken);
        var nextPurge = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (UtcNow >= nextPurge)
            {
                await PurgeAsync(stoppingToken);
                nextPurge = UtcNow + GenerationOperationOptions.PurgeInterval;
            }
            if (!await RunNextAsync(stoppingToken))
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), clock, stoppingToken);
        }
    }

    /// <summary>Called only after durable cancellation wins. Transport cancellation is best effort, never a billing guarantee.</summary>
    internal Task CancelTransportAsync(Guid id)
    {
        lock (transportLock) return callingId == id && transport is not null ? transport.CancelAsync() : Task.CompletedTask;
    }

    internal async Task<bool> RunNextAsync(CancellationToken ct)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var (found, call) = await ClaimAsync(cancellation, ct);
            if (call is null) return found;
            using var logScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["OperationId"] = call.Id,
                ["DraftId"] = call.DraftId,
                ["Stage"] = call.Stage
            });
            LogStageClaimed(logger);
            var evidence = new AiCallEvidence();
            Generated? generated = null;
            AiGenerationException? failure = null;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                generated = await GenerateAsync(call, evidence, cancellation.Token);
                var working = Advance(call, generated);
                if (NextStage(call.Kind, call.Stage, working) is null && working.Scope?.RequiresComplete == true && working.Scope.Clarification is null)
                {
                    var errors = TaskDocumentValidator.ValidateRelease(working.Input, working.Current);
                    if (errors.Count > 0) throw new TaskValidationException(errors);
                }
            }
            catch (AiGenerationException error) { failure = error; }
            catch (TaskValidationException error) { failure = AiGenerationException.InvalidOutput(error.Errors); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && cancellation.IsCancellationRequested) { }
            // Shutdown/database defects leave a durable calling claim for startup recovery, never an automatic retry.
            ct.ThrowIfCancellationRequested();
            await CheckpointAsync(call, evidence, generated, failure, ct);
            return true;
        }
        finally
        {
            lock (transportLock) { callingId = null; transport = null; }
        }
    }

    private async Task<(bool Found, ClaimedCall? Call)> ClaimAsync(CancellationTokenSource cancellation, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var operation = await db.GenerationOperations.Where(o => o.Status == "queued").OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id).FirstOrDefaultAsync(ct);
        if (operation is null) return (false, null);
        var draft = await db.ActivityDrafts.SingleAsync(d => d.Id == operation.DraftId, ct);
        if (!Compatible(operation) || !Matches(operation, draft))
            return await RejectAsync("conflict", Compatible(operation) ? "draft-changed" : "configuration-changed");
        var artifacts = operation.Artifacts;
        artifacts = artifacts with { Steps = [.. artifacts.Steps, new(operation.Stage, artifacts.Current)] };
        if (!operation.StoreArtifacts(artifacts, [.. operation.Steps, new(operation.Stage, "calling")]))
            return await RejectAsync("failed", "evidence-limit");
        operation.Claim();
        // Register before committing the claim so cancellation cannot slip between the claim and transport registration.
        lock (transportLock) { callingId = operation.Id; transport = cancellation; }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        changes.Publish(operation.FamilyId);
        return (true, new(operation.Id, operation.DraftId, operation.Kind, operation.Stage, artifacts));

        async Task<(bool, ClaimedCall?)> RejectAsync(string status, string failure)
        {
            operation.Finish(status, failure, UtcNow);
            draft.ClearOperation(operation.Id, UtcNow);
            draft.CompleteChat(operation, UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            changes.Publish(operation.FamilyId);
            LogOperationRejected(logger, operation.Id, operation.DraftId, failure);
            return (true, null);
        }
    }

    private async Task<Generated> GenerateAsync(ClaimedCall call, AiCallEvidence evidence, CancellationToken ct)
    {
        var input = call.Artifacts.Input;
        var current = call.Artifacts.Current;
        var scope = call.Artifacts.Scope;
        // Rebuilds retain old questions only as reference; their size/format cannot reject intermediate text.
        var materialCurrent = scope is null ? current : new TaskDocument("", null, current.Materials, []);
        TaskDocument PreserveReference(TaskDocument result) => scope is null ? result : current with { Materials = result.Materials };
        var materialInput = scope is null ? null : new MaterialGenerationInput(input, current.Materials, scope.NewMaterials);
        switch (call.Stage)
        {
            case "revise":
                var originalPlan = call.Artifacts.Plan;
                var revision = await ai.ReviseAsync(new(originalPlan, current, call.Artifacts.Instruction!, call.Artifacts.Target,
                    call.Artifacts.Sources, call.Artifacts.Context), ct, evidence);
                if (revision.Value.Change is not { } change)
                    return new(current, Candidate(revision.Value), Reply: revision.Value.Answer ?? revision.Value.Clarification);
                var work = RevisionScope.Derive(originalPlan, current, change);
                if (work.Clarification is { } clarification) return new(current, Candidate(revision.Value), Reply: clarification);
                var working = RevisionScope.PrepareDocument(originalPlan, current, change.Plan, work);
                return new(working, Candidate(revision.Value), Plan: change.Plan, Scope: work, Assumptions: change.Assumptions);
            case "material-ideas":
                var ideas = await ai.GenerateMaterialIdeasAsync(materialInput ?? TaskAssembly.PrepareMaterials(input, current)!, call.Artifacts.History.Ideas, ct, evidence);
                // The operation ID is the draw: an operation always selects the same idea, while operations vary.
                return new(current, Candidate(ideas.Value), MaterialIdeas.Select(ideas.Value, call.Id.GetHashCode()));
            case "materials":
                var idea = call.Artifacts.SelectedIdea ?? throw new InvalidOperationException("Materials are written only after an idea checkpoint.");
                var materials = await ai.GenerateMaterialsAsync(materialInput ?? TaskAssembly.PrepareMaterials(input, current)!, idea, ct, evidence);
                var accepted = TaskAssembly.AcceptMaterials(input, materialCurrent, materials.Value, materials.Metadata, idea, scope?.NewMaterials);
                return new(PreserveReference(accepted.Document ?? throw new TaskValidationException(accepted.Diagnostics)), Candidate(materials.Value));
            case "material-polish":
                var polishInput = new PolishInput(input, materialCurrent, scope?.NewMaterials);
                var polished = await ai.PolishMaterialsAsync(polishInput, ct, evidence);
                return new(PreserveReference(TaskAssembly.PolishMaterials(polishInput, polished.Value, polished.Metadata)), Candidate(polished.Value));
            case "questions":
                var questionRequest = TaskAssembly.PrepareQuestions(input, materialCurrent) with { Current = current, Instruction = scope?.Instruction };
                var questions = await ai.GenerateQuestionsAsync(questionRequest, call.Artifacts.History.Questions, ct, evidence);
                return new(TaskAssembly.AcceptQuestions(input, current, questions.Value, questions.Metadata), Candidate(questions.Value));
            case "append-questions":
                var additionInput = new QuestionAdditionInput(input, current, input.Settings.QuestionCount - current.Questions.Length, scope!.Instruction);
                var additions = await ai.AppendQuestionsAsync(additionInput, call.Artifacts.History.Questions, ct, evidence);
                return new(TaskAssembly.AppendQuestions(additionInput, additions.Value, additions.Metadata), Candidate(additions.Value));
            case "rewrite-material":
                var rewrite = scope!.Rewrites[call.Artifacts.RewriteIndex];
                var rewriteInput = new MaterialReplacementInput(input, materialCurrent, rewrite.Id, rewrite.Instruction,
                    scope.Rewrites.Skip(call.Artifacts.RewriteIndex + 1).Select(r => r.Id).ToArray());
                var rewritten = await ai.ReplaceMaterialAsync(rewriteInput, ct, evidence);
                return new(PreserveReference(TaskAssembly.ReplaceMaterial(rewriteInput, rewritten.Value, rewritten.Metadata)), Candidate(rewritten.Value));
            case "revise-question":
                var edit = scope!.QuestionEdits[call.Artifacts.QuestionIndex];
                var replacement = new QuestionReplacementInput(input, current, edit.Id, edit.Instruction);
                var replaced = await ai.ReplaceQuestionAsync(replacement, ct, evidence);
                return new(TaskAssembly.ReplaceQuestion(replacement, replaced.Value, replaced.Metadata), Candidate(replaced.Value));
            case "replace-material":
                var replacementInput = new MaterialReplacementInput(input, current, call.Artifacts.TargetId!, call.Artifacts.Instruction);
                var material = await ai.ReplaceMaterialAsync(replacementInput, ct, evidence);
                return new(TaskAssembly.ReplaceMaterial(replacementInput, material.Value, material.Metadata), Candidate(material.Value));
            case "replace-question":
                var questionInput = new QuestionReplacementInput(input, current, call.Artifacts.TargetId!, call.Artifacts.Instruction);
                var question = await ai.ReplaceQuestionAsync(questionInput, ct, evidence);
                return new(TaskAssembly.ReplaceQuestion(questionInput, question.Value, question.Metadata), Candidate(question.Value));
            default: throw new InvalidOperationException("Unsupported stored generation stage.");
        }
    }

    private async Task CheckpointAsync(ClaimedCall call, AiCallEvidence evidence, Generated? generated, AiGenerationException? failure, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var operation = await db.GenerationOperations.SingleOrDefaultAsync(o => o.Id == call.Id, ct);
        if (operation is null) return; // Draft deletion/reset cascaded its operation and key.
        var draft = await db.ActivityDrafts.SingleAsync(d => d.Id == operation.DraftId, ct);
        var summary = new GenerationStep(call.Stage, "returned", evidence.Usage, evidence.Metadata);
        if (operation.Status == "cancelled")
        {
            operation.RecordLateUsage(summary);
        }
        else
        {
            var conflict = operation.Status != "calling" || !Matches(operation, draft);
            var outcome = conflict ? "conflict" : failure is not null || generated is null ? "failed" : "accepted";
            var artifacts = operation.Artifacts;
            artifacts.Steps[^1] = artifacts.Steps[^1] with { Call = evidence, Candidate = generated?.Candidate, Diagnostics = failure?.ValidationErrors };
            var steps = operation.Steps;
            steps[^1] = summary with { Outcome = outcome };
            var acceptedArtifacts = outcome == "accepted" ? Advance(call with { Artifacts = artifacts }, generated!) : artifacts;
            if (!operation.StoreArtifacts(acceptedArtifacts, steps))
            {
                operation.RecordResult(summary with { Outcome = "failed" });
                operation.Finish("failed", "evidence-limit", UtcNow);
            }
            else if (conflict) operation.Finish("conflict", "draft-changed", UtcNow);
            else if (failure is not null || generated is null) operation.Finish("failed", failure?.Category ?? "cancelled-call", UtcNow);
            else
            {
                if (NextStage(call.Kind, call.Stage, acceptedArtifacts) is { } next) operation.QueueStage(next, draft.Revision);
                else
                {
                    var changed = draft.ApplyOperation(acceptedArtifacts.Plan, acceptedArtifacts.Current, call.Kind);
                    operation.Finish("completed", null, UtcNow, draft.Revision);
                    draft.CompleteChat(operation, UtcNow, acceptedArtifacts.Reply ?? (changed ? null : "הפעולה הסתיימה ללא שינוי בתוכן."), acceptedArtifacts.Assumptions);
                }
            }
            if (operation.Status != "queued")
            {
                draft.ClearOperation(operation.Id, UtcNow);
                draft.CompleteChat(operation, UtcNow);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        changes.Publish(operation.FamilyId);
        LogStageCheckpointed(logger, operation.Status is "failed" or "conflict" ? LogLevel.Warning : LogLevel.Information,
            operation.Status, operation.Failure);
    }

    internal async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var operations = await db.GenerationOperations.Where(o => o.Status == "calling" || o.Status == "queued")
            .OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id)
            .Take(GenerationOperationOptions.GlobalLimit).ToListAsync(ct);
        foreach (var operation in operations)
        {
            if (operation.Status == "queued" && Compatible(operation)) continue;
            if (operation.Status == "calling") operation.MarkInterruptedStep("unknown");
            operation.Finish(operation.Status == "calling" ? "unknown" : "conflict",
                operation.Status == "calling" ? "interrupted-call" : "configuration-changed", UtcNow);
            var draft = await db.ActivityDrafts.SingleAsync(d => d.Id == operation.DraftId, ct);
            draft.ClearOperation(operation.Id, UtcNow);
            draft.CompleteChat(operation, UtcNow);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var operation in operations) changes.Publish(operation.FamilyId);
        foreach (var operation in operations)
            if (operation.Status is "unknown" or "conflict")
                LogOperationRecovered(logger, operation.Id, operation.DraftId, operation.Status, operation.Failure);
    }

    internal async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var cutoff = UtcNow - GenerationOperationOptions.ArtifactRetention;
        var expired = await db.GenerationOperations.Where(o => o.FinishedAtUtc <= cutoff && o.ArtifactsJson != null)
            .OrderBy(o => o.FinishedAtUtc).Select(o => new { o.Id, o.FamilyId }).Take(GenerationOperationOptions.PurgeBatchSize).ToListAsync(ct);
        var ids = expired.Select(o => o.Id).ToArray();
        // Avoid loading up to 64 MiB just to discard it; concurrent draft deletion is harmless.
        var purged = await db.GenerationOperations.Where(o => ids.Contains(o.Id) && o.FinishedAtUtc <= cutoff)
            .ExecuteUpdateAsync(update => update.SetProperty(o => o.ArtifactsJson, (string?)null), ct);
        if (purged > 0)
        {
            foreach (var familyId in expired.Select(o => o.FamilyId).Distinct()) changes.Publish(familyId);
            LogArtifactsExpired(logger, purged);
        }
    }

    private bool Compatible(GenerationOperation operation) => operation.EngineRevision == EngineVersions.Revision &&
        operation.SchemaVersion == EngineVersions.SchemaVersion && operation.ProfileFingerprint == ProfileFingerprint;

    private static bool Matches(GenerationOperation operation, ActivityDraft draft) => draft.FamilyId == operation.FamilyId &&
        draft.ReleasedSnapshotId is null && draft.ActiveOperationId == operation.Id && draft.Revision == operation.ExpectedRevision;

    private static JsonElement Candidate<T>(T value) => JsonSerializer.SerializeToElement(value, EngineJson.Options);

    [LoggerMessage(1001, LogLevel.Debug, "Generation stage claimed")]
    private static partial void LogStageClaimed(ILogger logger);

    [LoggerMessage(1002, LogLevel.Warning, "Generation operation {OperationId} for draft {DraftId} rejected before calling: {Failure}")]
    private static partial void LogOperationRejected(ILogger logger, Guid operationId, Guid draftId, string failure);

    [LoggerMessage(EventId = 1003, Message = "Generation stage checkpointed with status {Status} and failure {Failure}")]
    private static partial void LogStageCheckpointed(ILogger logger, LogLevel level, string status, string? failure);

    [LoggerMessage(1004, LogLevel.Warning, "Generation operation {OperationId} for draft {DraftId} recovered as {Status}: {Failure}")]
    private static partial void LogOperationRecovered(ILogger logger, Guid operationId, Guid draftId, string status, string? failure);

    [LoggerMessage(1005, LogLevel.Debug, "Expired generation artifacts for {OperationCount} operations")]
    private static partial void LogArtifactsExpired(ILogger logger, int operationCount);

    private static GenerationArtifacts Advance(ClaimedCall call, Generated generated)
    {
        var artifacts = call.Artifacts;
        return artifacts with
        {
            Plan = generated.Plan ?? artifacts.Plan,
            Input = generated.Plan is null ? artifacts.Input : TaskRequestResolver.ResolveOrThrow(generated.Plan),
            Current = generated.Document,
            Scope = generated.Scope ?? artifacts.Scope,
            Reply = generated.Reply ?? artifacts.Reply,
            Assumptions = generated.Assumptions ?? artifacts.Assumptions,
            SelectedIdea = generated.SelectedIdea ?? artifacts.SelectedIdea,
            RewriteIndex = artifacts.RewriteIndex + (call.Stage == "rewrite-material" ? 1 : 0),
            QuestionIndex = artifacts.QuestionIndex + (call.Stage == "revise-question" ? 1 : 0)
        };
    }

    // Fixed sequence with bounded cursors; checkpoints never contain executable work queues.
    private static string? NextStage(string kind, string stage, GenerationArtifacts artifacts)
    {
        if (stage == "material-ideas") return "materials";
        if (stage == "materials") return "material-polish";
        if (kind is not ("Create" or "Revise") || artifacts.Reply is not null || artifacts.Scope is not { } work) return null;
        if (stage is "revise" or "rewrite-material")
        {
            if (artifacts.RewriteIndex < work.Rewrites.Length) return "rewrite-material";
            if (work.NewMaterials.Length > 0) return "material-ideas";
        }
        if (stage is "revise" or "rewrite-material" or "material-polish" or "revise-question")
            return work.Questions switch
            {
                "all" => "questions",
                "append" => "append-questions",
                "selected" when artifacts.QuestionIndex < work.QuestionEdits.Length => "revise-question",
                _ => null
            };
        return null;
    }

    private sealed record ClaimedCall(Guid Id, Guid DraftId, string Kind, string Stage, GenerationArtifacts Artifacts);
    private sealed record Generated(TaskDocument Document, JsonElement Candidate, MaterialIdea? SelectedIdea = null,
        LearningPlan? Plan = null, RevisionWork? Scope = null, string? Reply = null, string[]? Assumptions = null);
}
