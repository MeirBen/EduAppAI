using System.Security.Claims;
using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Parent-only durable start, status and cancel routes. Database transactions serialize admission; polling never starts work.</summary>
public static class GenerationOperationEndpoints
{
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
        GenerationWorker worker, TimeProvider clock, IDiagnosticContext diagnostics, CancellationToken ct)
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
        diagnostics.Set("OperationId", operation.Id);
        diagnostics.Set("DraftId", operation.DraftId);
        diagnostics.Set("Status", operation.Status);
        return Results.Ok(GenerationOperationDetail.From(operation));
    }

    private static async Task<IResult> StartAsync(Guid id, StartGenerationRequest body, ClaimsPrincipal user,
        LearningDbContext db, AiStartLimiter limiter, GenerationWorker worker, TimeProvider clock, IDiagnosticContext diagnostics, CancellationToken ct)
    {
        // SQLite's immediate write transaction makes count admission, key insertion and draft assignment one decision.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var familyId = user.FamilyId();
        var draft = await db.ActivityDrafts.SingleOrDefaultAsync(d => d.Id == id && d.FamilyId == familyId, ct);
        if (draft is null) return Results.NotFound();
        var existing = await db.GenerationOperations.SingleOrDefaultAsync(o => o.FamilyId == familyId && o.OperationKey == body.OperationKey, ct);
        if (existing is not null)
            return existing.RequestFingerprint == GenerationOperation.Fingerprint(id, body) ? Accepted(existing, diagnostics) : Conflict();
        if (body.OperationKey == Guid.Empty || body.Kind is not ("Create" or "Revise" or "GenerateQuestions") ||
            body.Kind != "Revise" && (body.Message is not null || body.Target is not null))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["operation"] = ["יש לבחור פעולת יצירה והנחיה תקינות."] });
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (await db.GenerationOperations.CountAsync(o => o.DraftId == id, ct) >= GenerationOperationOptions.DraftLimit ||
            await db.GenerationOperations.CountAsync(o => o.Status == "queued" || o.Status == "calling", ct) >= GenerationOperationOptions.GlobalLimit ||
            await db.GenerationOperations.CountAsync(o => o.FamilyId == familyId && (o.Status == "queued" || o.Status == "calling"), ct) >= GenerationOperationOptions.FamilyLimit)
            return Results.Problem(statusCode: 429, title: "מכסת פעולות ה־AI מלאה. אפשר להמשיך לשמור עריכות ידניות ולעיין בפעילות לפני אישור.");
        var plan = draft.Plan;
        var request = TaskRequestResolver.ResolveOrThrow(plan);
        var document = draft.Document;
        if (body.Kind == "Revise") ActivityRevisionValidator.ValidateInput(new(plan, document, body.Message!, body.Target));
        var stage = SelectStage(body, request, document, plan);
        if (!limiter.TryAcquire(familyId)) return Results.StatusCode(429);
        var history = await GenerationHistoryReader.ReadAsync(db, familyId, draft.Id, document, ct);
        var operation = new GenerationOperation(draft, body, request, document, history, stage, worker.ProfileFingerprint, clock.GetUtcNow().UtcDateTime);
        if (body.Kind == "Revise") draft.AppendTurn(new("parent", body.Message!, clock.GetUtcNow().UtcDateTime, body.Target, operation.Id));
        draft.StartOperation(operation.Id);
        db.GenerationOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Accepted(operation, diagnostics);
    }

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

    private static IResult Accepted(GenerationOperation operation, IDiagnosticContext diagnostics)
    {
        diagnostics.Set("OperationId", operation.Id);
        diagnostics.Set("DraftId", operation.DraftId);
        diagnostics.Set("Status", operation.Status);
        return Results.Accepted($"/api/activity-drafts/{operation.DraftId}/operations/{operation.Id}", GenerationOperationDetail.From(operation));
    }
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "בקשת היצירה או הטיוטה השתנתה. יש לטעון את המצב השמור.");
}
