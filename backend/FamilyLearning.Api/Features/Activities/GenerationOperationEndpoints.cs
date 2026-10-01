using System.Security.Claims;
using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Owned durable starts and polling. Database transactions serialize admission; reads never start work.</summary>
public static class GenerationOperationEndpoints
{
    /// <summary>Maps parent-only start, status and cancel routes; polling never initiates a provider call.</summary>
    public static void MapGenerationOperationEndpoints(this RouteGroupBuilder api)
    {
        var operations = api.MapGroup("/activity-drafts/{id:guid}/operations");
        operations.MapPost("/", StartAsync);
        operations.MapPost("/{operationId:guid}/cancel", CancelAsync);
        operations.MapGet("/{operationId:guid}", async (Guid id, Guid operationId, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var operation = await db.GenerationOperations.AsNoTracking().SingleOrDefaultAsync(o =>
                o.Id == operationId && o.DraftId == id && o.FamilyId == user.FamilyId(), ct);
            return operation is null ? Results.NotFound() : Results.Ok(GenerationOperationDetail.From(operation));
        });
    }

    private static async Task<IResult> CancelAsync(Guid id, Guid operationId, ClaimsPrincipal user, LearningDbContext db,
        GenerationWorker worker, TimeProvider clock, CancellationToken ct)
    {
        GenerationOperation operation;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var draft = await db.ActivityDrafts.SingleOrDefaultAsync(d => d.Id == id && d.FamilyId == user.FamilyId(), ct);
            if (draft is null) return Results.NotFound();
            var stored = await db.GenerationOperations.SingleOrDefaultAsync(o => o.Id == operationId && o.DraftId == id && o.FamilyId == draft.FamilyId, ct);
            if (stored is null) return Results.NotFound();
            operation = stored;
            if (operation.Status is "queued" or "calling")
            {
                var now = clock.GetUtcNow().UtcDateTime;
                operation.Finish("cancelled", null, now);
                operation.MarkInterruptedStep("cancelled");
                draft.ClearOperation(operation.Id, now, cancelled: true);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        // Durable cancellation wins before touching transport; a late completion can only attach known usage.
        if (operation.Status == "cancelled") await worker.CancelTransportAsync(operation.Id);
        return Results.Ok(GenerationOperationDetail.From(operation));
    }

    private static async Task<IResult> StartAsync(Guid id, StartGenerationRequest body, ClaimsPrincipal user,
        LearningDbContext db, AiStartLimiter limiter, GenerationWorker worker, TimeProvider clock, CancellationToken ct)
    {
        // SQLite's immediate write transaction makes count admission, key insertion and draft assignment one decision.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var familyId = user.FamilyId();
        var draft = await db.ActivityDrafts.SingleOrDefaultAsync(d => d.Id == id && d.FamilyId == familyId, ct);
        if (draft is null) return Results.NotFound();
        var existing = await db.GenerationOperations.SingleOrDefaultAsync(o => o.FamilyId == familyId && o.OperationKey == body.OperationKey, ct);
        if (existing is not null)
            return existing.RequestFingerprint == GenerationOperation.Fingerprint(id, body) ? Accepted(existing) : Conflict();
        if (body.OperationKey == Guid.Empty || body.Kind is not ("GenerateActivity" or "GenerateQuestions" or "ReplaceMaterial" or "ReplaceQuestion") ||
            (body.Kind is "GenerateActivity" or "GenerateQuestions" && (body.TargetId is not null || body.Instruction is not null)))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["operation"] = ["יש לבחור פעולת יצירה והנחיה תקינות."] });
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (await db.GenerationOperations.CountAsync(o => o.DraftId == id, ct) >= GenerationOperationOptions.DraftLimit ||
            await db.GenerationOperations.CountAsync(o => o.Status == "queued" || o.Status == "calling", ct) >= GenerationOperationOptions.GlobalLimit ||
            await db.GenerationOperations.CountAsync(o => o.FamilyId == familyId && (o.Status == "queued" || o.Status == "calling"), ct) >= GenerationOperationOptions.FamilyLimit)
            return Results.Problem(statusCode: 429, title: "מכסת פעולות היצירה מלאה. אפשר להמשיך לערוך או ליצור עותק חדש במפורש.");
        var input = TaskRequestResolver.Resolve(StoredJson.Read<LearningPlan>(draft.PlanJson), StoredJson.Read<TaskRequest>(draft.InputJson));
        var request = input.Value ?? throw new TaskValidationException(input.Errors);
        var document = StoredJson.Read<TaskDocument>(draft.DocumentJson);
        var stage = SelectStage(body, request, document);
        if (!limiter.TryAcquire(familyId)) return Results.StatusCode(429);
        var operation = new GenerationOperation(draft, body, request, document, stage, worker.ProfileFingerprint, clock.GetUtcNow().UtcDateTime);
        draft.StartOperation(operation.Id);
        db.GenerationOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Accepted(operation);
    }

    internal static string SelectStage(StartGenerationRequest body, ResolvedTaskRequest input, TaskDocument document)
    {
        switch (body.Kind)
        {
            case "GenerateActivity" when TaskAssembly.PrepareMaterials(input, document) is not null: return "materials";
            case "GenerateActivity":
            case "GenerateQuestions": TaskAssembly.PrepareQuestions(input, document); return "questions";
            case "ReplaceMaterial": TaskAssembly.MaterialTarget(new(input, document, body.TargetId!, body.Instruction)); return "replace-material";
            case "ReplaceQuestion": TaskAssembly.QuestionTarget(new(input, document, body.TargetId!, body.Instruction)); return "replace-question";
            default: throw new InvalidOperationException("Unsupported stored generation kind.");
        }
    }

    private static IResult Accepted(GenerationOperation operation) => Results.Accepted(
        $"/api/activity-drafts/{operation.DraftId}/operations/{operation.Id}", GenerationOperationDetail.From(operation));
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "בקשת היצירה או הטיוטה השתנתה. יש לטעון את המצב השמור.");
}
