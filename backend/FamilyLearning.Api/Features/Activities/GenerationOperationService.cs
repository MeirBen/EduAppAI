using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Durable generation admission, status and cancellation serialized with draft state.</summary>
internal sealed class GenerationOperationService(LearningDbContext db, AiStartLimiter limiter, GenerationWorker worker, TimeProvider clock)
{
    internal async Task<FeatureResult<GenerationOperationDetail>> FindAsync(Guid familyId, Guid id, Guid operationId, CancellationToken ct)
    {
        var operation = await db.GenerationOperations.AsNoTracking().SingleOrDefaultAsync(o =>
            o.Id == operationId && o.DraftId == id && o.FamilyId == familyId, ct);
        return operation is null ? FeatureFailure.NotFound : Detail(operation);
    }

    internal async Task<FeatureResult<GenerationOperationDetail>> CancelAsync(Guid familyId, Guid id, Guid operationId, CancellationToken ct)
    {
        GenerationOperation operation;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var draft = await db.ActivityDrafts.SingleOrDefaultAsync(d => d.Id == id && d.FamilyId == familyId, ct);
            if (draft is null) return FeatureFailure.NotFound;
            var stored = await db.GenerationOperations.SingleOrDefaultAsync(o => o.Id == operationId && o.DraftId == id && o.FamilyId == draft.FamilyId, ct);
            if (stored is null) return FeatureFailure.NotFound;
            operation = stored;
            if (operation.Status is "queued" or "calling")
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var unchanged = draft.ActiveOperationId == operation.Id && draft.Revision == operation.ExpectedRevision;
                operation.MarkInterruptedStep("cancelled");
                draft.ClearOperation(operation.Id, now);
                operation.Finish("cancelled", null, now, unchanged ? draft.Revision : null);
                draft.CompleteChat(operation, now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        // Durable cancellation wins before touching transport; a late completion can only attach known usage.
        if (operation.Status == "cancelled") await worker.CancelTransportAsync(operation.Id);
        return Detail(operation);
    }

    internal async Task<FeatureResult<GenerationOperationDetail>> StartAsync(Guid familyId, Guid id, StartGenerationRequest body, CancellationToken ct)
    {
        // SQLite's immediate write transaction makes count admission, key insertion and draft assignment one decision.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var draft = await db.ActivityDrafts.SingleOrDefaultAsync(d => d.Id == id && d.FamilyId == familyId, ct);
        if (draft is null) return FeatureFailure.NotFound;
        var existing = await db.GenerationOperations.SingleOrDefaultAsync(o => o.FamilyId == familyId && o.OperationKey == body.OperationKey, ct);
        if (existing is not null)
            return existing.RequestFingerprint == GenerationOperation.Fingerprint(id, body) ? Detail(existing) : Conflict();
        if (body.OperationKey == Guid.Empty || body.Kind is not ("Create" or "Revise" or "GenerateQuestions") ||
            body.Kind != "Revise" && (body.Message is not null || body.Target is not null))
            return FeatureFailure.Invalid("operation", "יש לבחור פעולת יצירה והנחיה תקינות.");
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (await db.GenerationOperations.CountAsync(o => o.DraftId == id, ct) >= GenerationOperationOptions.DraftLimit ||
            await db.GenerationOperations.CountAsync(o => o.Status == "queued" || o.Status == "calling", ct) >= GenerationOperationOptions.GlobalLimit ||
            await db.GenerationOperations.CountAsync(o => o.FamilyId == familyId && (o.Status == "queued" || o.Status == "calling"), ct) >= GenerationOperationOptions.FamilyLimit)
            return FeatureFailure.Busy("מכסת פעולות ה־AI מלאה. אפשר להמשיך לשמור עריכות ידניות ולעיין בפעילות לפני אישור.");
        var plan = draft.Plan;
        var request = TaskRequestResolver.ResolveOrThrow(plan);
        var document = draft.Document;
        if (body.Kind == "Revise") ActivityRevisionValidator.ValidateInput(new(plan, document, body.Message!, body.Target));
        var stage = SelectStage(body, request, document, plan);
        if (!limiter.TryAcquire(familyId)) return FeatureFailure.Busy();
        var history = await GenerationHistoryReader.ReadAsync(db, familyId, draft.Id, document, ct);
        var operation = new GenerationOperation(draft, body, request, document, history, stage, worker.ProfileFingerprint, clock.GetUtcNow().UtcDateTime);
        if (body.Kind == "Revise") draft.AppendTurn(new("parent", body.Message!, clock.GetUtcNow().UtcDateTime, body.Target, operation.Id));
        draft.StartOperation(operation.Id);
        db.GenerationOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Detail(operation);
    }

    private static GenerationOperationDetail Detail(GenerationOperation operation) => new(operation.Id, operation.DraftId,
        operation.Kind, operation.Status, operation.Stage, operation.OriginalRevision, operation.ExpectedRevision, operation.Failure,
        operation.ArtifactsJson is null, operation.Steps, operation.ArtifactsJson is { } json ? StoredJson.Read<GenerationArtifacts>(json) : null);

    private static string SelectStage(StartGenerationRequest body, ResolvedTaskRequest input, TaskDocument document, LearningPlan plan)
    {
        switch (body.Kind)
        {
            case "Revise": return "revise";
            case "Create": return RevisionScope.ForCreate(plan, document).NewMaterials.Length > 0 ? "material-ideas" : "questions";
            case "GenerateQuestions": TaskAssembly.PrepareQuestions(input, document); return "questions";
            default: throw new InvalidOperationException("Unsupported stored generation kind.");
        }
    }

    private static FeatureFailure Conflict() => FeatureFailure.Conflict("בקשת היצירה או הטיוטה השתנתה. יש לטעון את המצב השמור.");
}
