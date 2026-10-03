using System.Security.Cryptography;
using System.Text.Json;
using FamilyLearning.Api.Infrastructure.Ai;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>One process, one sequential content caller. SQLite checkpoints own recovery; no transaction crosses a provider call.</summary>
public sealed class GenerationWorker(IServiceScopeFactory scopes, AiGenerationService ai, TimeProvider clock,
    IOptions<GenerationOperationOptions> options, IOptions<AiGenerationOptions> aiOptions, IConfiguration configuration) : BackgroundService
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
            var evidence = new AiCallEvidence();
            Generated? generated = null;
            AiGenerationException? failure = null;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                generated = await GenerateAsync(call, evidence, cancellation.Token);
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
        {
            operation.Finish("conflict", Compatible(operation) ? "draft-changed" : "configuration-changed", UtcNow);
            draft.ClearOperation(operation.Id, UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (true, null);
        }
        var artifacts = operation.Artifacts;
        var steps = operation.Steps;
        var stage = new GenerationStepArtifact(operation.Stage, artifacts.Current);
        artifacts = artifacts with { Steps = [.. artifacts.Steps, stage] };
        if (!operation.StoreArtifacts(artifacts, [.. steps, new(operation.Stage, "calling")]))
        {
            operation.Finish("failed", "evidence-limit", UtcNow);
            draft.ClearOperation(operation.Id, UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (true, null);
        }
        operation.Claim();
        // Register before committing the claim so cancellation cannot slip between the claim and transport registration.
        lock (transportLock) { callingId = operation.Id; transport = cancellation; }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (true, new(operation.Id, operation.Stage, artifacts));
    }

    private async Task<Generated> GenerateAsync(ClaimedCall call, AiCallEvidence evidence, CancellationToken ct)
    {
        var input = call.Artifacts.Input;
        var current = call.Artifacts.Current;
        switch (call.Stage)
        {
            case "materials":
                var materials = await ai.GenerateMaterialsAsync(TaskAssembly.PrepareMaterials(input, current)!, ct, evidence);
                var accepted = TaskAssembly.AcceptMaterials(input, current, materials.Value, materials.Metadata);
                return new(accepted.Document ?? throw new TaskValidationException(accepted.Diagnostics), JsonSerializer.SerializeToElement(materials.Value, EngineJson.Options));
            case "questions":
                var questions = await ai.GenerateQuestionsAsync(TaskAssembly.PrepareQuestions(input, current), ct, evidence);
                return new(TaskAssembly.AcceptQuestions(input, current, questions.Value, questions.Metadata), JsonSerializer.SerializeToElement(questions.Value, EngineJson.Options));
            case "replace-material":
                var materialInput = new MaterialReplacementInput(input, current, call.Artifacts.TargetId!, call.Artifacts.Instruction);
                var material = await ai.ReplaceMaterialAsync(materialInput, ct, evidence);
                return new(TaskAssembly.ReplaceMaterial(materialInput, material.Value, material.Metadata), JsonSerializer.SerializeToElement(material.Value, EngineJson.Options));
            case "replace-question":
                var questionInput = new QuestionReplacementInput(input, current, call.Artifacts.TargetId!, call.Artifacts.Instruction);
                var question = await ai.ReplaceQuestionAsync(questionInput, ct, evidence);
                return new(TaskAssembly.ReplaceQuestion(questionInput, question.Value, question.Metadata), JsonSerializer.SerializeToElement(question.Value, EngineJson.Options));
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
            var acceptedArtifacts = outcome == "accepted" ? artifacts with { Current = generated!.Document } : artifacts;
            if (!operation.StoreArtifacts(acceptedArtifacts, steps))
            {
                operation.RecordResult(summary with { Outcome = "failed" });
                operation.Finish("failed", "evidence-limit", UtcNow);
            }
            else if (conflict) operation.Finish("conflict", "draft-changed", UtcNow);
            else if (failure is not null || generated is null) operation.Finish("failed", failure?.Category ?? "cancelled-call", UtcNow);
            else
            {
                draft.Save(draft.Name, draft.PlanJson, draft.InputJson, StoredJson.Write(generated.Document));
                if (call.Stage == "materials" && operation.Kind == "GenerateActivity")
                {
                    // The accepted content and next stage are in this same commit; recovery never repeats the material call.
                    operation.QueueQuestions(draft.Revision);
                }
                else operation.Finish("completed", null, UtcNow);
            }
            if (operation.Status != "queued") draft.ClearOperation(operation.Id, UtcNow);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    internal async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        var cutoff = UtcNow - GenerationOperationOptions.ArtifactRetention;
        var expired = await db.GenerationOperations.Where(o => o.FinishedAtUtc <= cutoff && o.ArtifactsJson != null)
            .OrderBy(o => o.FinishedAtUtc).Select(o => o.Id).Take(GenerationOperationOptions.PurgeBatchSize).ToListAsync(ct);
        // Avoid loading up to 64 MiB just to discard it; concurrent draft deletion is harmless.
        await db.GenerationOperations.Where(o => expired.Contains(o.Id) && o.FinishedAtUtc <= cutoff)
            .ExecuteUpdateAsync(update => update.SetProperty(o => o.ArtifactsJson, (string?)null), ct);
    }

    private bool Compatible(GenerationOperation operation) => operation.EngineRevision == EngineVersions.Revision &&
        operation.SchemaVersion == EngineVersions.SchemaVersion && operation.ProfileFingerprint == ProfileFingerprint;

    private static bool Matches(GenerationOperation operation, ActivityDraft draft) => draft.FamilyId == operation.FamilyId &&
        draft.ReleasedSnapshotId is null && draft.ActiveOperationId == operation.Id && draft.Revision == operation.ExpectedRevision;

    private sealed record ClaimedCall(Guid Id, string Stage, GenerationArtifacts Artifacts);
    private sealed record Generated(TaskDocument Document, JsonElement Candidate);
}

/// <summary>Registers the single-process content worker with existing persistence/provider services and validated polling options.</summary>
public static class ActivityGenerationRegistration
{
    public static void AddActivityGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AiGenerationService>();
        services.AddOptions<GenerationOperationOptions>().Bind(configuration.GetSection("GenerationOperations"))
            .Validate(o => o.PollIntervalSeconds is >= 1 and <= 60, "GenerationOperations:PollIntervalSeconds must be between 1 and 60.").ValidateOnStart();
        services.AddSingleton(p => new GenerationWorker(p.GetRequiredService<IServiceScopeFactory>(), p.GetRequiredService<AiGenerationService>(),
            p.GetRequiredService<TimeProvider>(), p.GetRequiredService<IOptions<GenerationOperationOptions>>(), p.GetRequiredService<IOptions<AiGenerationOptions>>(), configuration));
        services.AddHostedService(p => p.GetRequiredService<GenerationWorker>());
    }
}
