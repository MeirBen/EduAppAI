using System.Security.Claims;
using System.Text.Json;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Parent-only draft checkpoints, adoption and immutable release. No endpoint in this feature calls a provider.</summary>
public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this RouteGroupBuilder api)
    {
        var drafts = api.MapGroup("/activity-drafts");
        drafts.MapPost("/", CreateAsync);
        drafts.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.ActivityDrafts.AsNoTracking().Where(d => d.FamilyId == user.FamilyId() && d.ReleasedSnapshotId == null)
                .OrderByDescending(d => d.UpdatedAtUtc).Take(EngineValidation.ListLimit)
                .Select(d => new ActivitySummary(d.Id, d.Name, d.Revision, d.UpdatedAtUtc)).ToListAsync(ct));
        drafts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var draft = await Owned(db, user, id).AsNoTracking().SingleOrDefaultAsync(ct);
            return draft is null ? Results.NotFound() : Results.Ok(ActivityDetail.From(draft));
        });
        drafts.MapPut("/{id:guid}", SaveAsync);
        drafts.MapPost("/{id:guid}/adopt-content", AdoptAsync);
        drafts.MapPost("/{id:guid}/release", ReleaseAsync);
        drafts.MapPost("/{id:guid}/undo", UndoAsync);
        drafts.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await Owned(db, user, id).ExecuteDeleteAsync(ct) == 0 ? Results.NotFound() : Results.NoContent());
    }

    private static async Task<IResult> CreateAsync(CreateActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        LearningPlan plan;
        TaskDocument? document = null;
        Guid? sourceSnapshotId = null;
        if (body.SnapshotId.ValueKind != JsonValueKind.Undefined)
        {
            if (body.Chat is not null || body.Plan.ValueKind != JsonValueKind.Undefined)
                return Invalid("snapshotId", "יש לבחור מקור אחד ליצירת הטיוטה.");
            var snapshotId = Read<Guid>(body.SnapshotId);
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.FamilyId == user.FamilyId(), ct);
            if (snapshot is null) return Results.NotFound();
            plan = StoredJson.Read<LearningPlan>(snapshot.PlanJson);
            document = StoredJson.Read<TaskDocument>(snapshot.DocumentJson);
            sourceSnapshotId = snapshot.Id;
        }
        else plan = Read<LearningPlan>(body.Plan);
        // Copying a snapshot keeps content/provenance, but never copies its parent review or terminal state.
        var resolved = TaskRequestResolver.ResolveOrThrow(plan);
        document ??= TaskAssembly.CreateDocument(resolved);
        var errors = TaskDocumentValidator.ValidateDraft(resolved, document).Errors;
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var draft = new ActivityDraft(user.FamilyId(), ActivityDraft.LibraryName(plan, document), StoredJson.Write(plan),
            StoredJson.Write(document), sourceSnapshotId, user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        draft.ImportChat(body.Chat);
        db.ActivityDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/activity-drafts/{draft.Id}", ActivityDetail.From(draft));
    }

    private static async Task<IResult> SaveAsync(Guid id, SaveActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var draft = await Owned(db, user, id).SingleOrDefaultAsync(ct);
        if (draft is null) return Results.NotFound();
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        var (plan, document) = ActivityDraftChanges.Apply(draft, body);
        draft.Save(plan, document);
        return await SaveCheckpointAsync(draft, db, user, ct);
    }

    private static async Task<IResult> AdoptAsync(Guid id, AdoptActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var draft = await Owned(db, user, id).SingleOrDefaultAsync(ct);
        if (draft is null) return Results.NotFound();
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (body.MaterialIds is not { Length: <= EngineValidation.MaxMaterials } || body.QuestionIds is not { Length: <= EngineValidation.MaxQuestionCount } ||
            body.MaterialIds.Length + body.QuestionIds.Length == 0)
            return Invalid("selection", "יש לבחור תוכן לבדיקה ולאימוץ.");
        var request = TaskRequestResolver.ResolveOrThrow(draft.Plan);
        var document = TaskAssembly.Adopt(request, draft.Document, body.MaterialIds, body.QuestionIds, DateTime.UtcNow);
        draft.Save(draft.Plan, document);
        return await SaveCheckpointAsync(draft, db, user, ct);
    }

    private static async Task<IResult> UndoAsync(Guid id, UndoActivityRequest body, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        // Reply-only operations can leave the revision unchanged; serialize reading and extending the chat with their writes.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var draft = await Owned(db, user, id).SingleOrDefaultAsync(ct);
        if (draft is null) return Results.NotFound();
        if (draft.Revision != body.ExpectedRevision || !draft.CanUndo) return Conflict();
        draft.RestoreUndo(clock.GetUtcNow().UtcDateTime);
        var result = await SaveCheckpointAsync(draft, db, user, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<IResult> SaveCheckpointAsync(ActivityDraft draft, LearningDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return await Owned(db, user, draft.Id).AsNoTracking().AnyAsync(ct) ? Conflict() : Results.NotFound();
        }
        return Results.Ok(ActivityDetail.From(draft));
    }

    private static async Task<IResult> ReleaseAsync(Guid id, ReleaseActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var draft = await Owned(db, user, id).SingleOrDefaultAsync(ct);
        if (draft is null) return Results.NotFound();
        if (draft.ReleasedSnapshotId.HasValue) return await ReplayAsync(draft, body.ExpectedRevision, db, ct);
        if (draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        var input = TaskRequestResolver.ResolveOrThrow(draft.Plan);
        var document = draft.Document;
        var errors = TaskDocumentValidator.ValidateRelease(input, document);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var reviewedAt = DateTime.UtcNow;
        var snapshot = new TaskSnapshot(draft.FamilyId, draft.Id, draft.Revision, document.Title, draft.PlanJson, draft.DocumentJson, StoredJson.Write(TextLength.Measure(input, document)), EngineVersions.Revision,
            draft.SourceSnapshotId, draft.CreatedByParentId, draft.CreatedAtUtc,
            user.FindFirstValue(ClaimTypes.NameIdentifier)!, reviewedAt);
        draft.Release(snapshot.Id, reviewedAt);
        db.TaskSnapshots.Add(snapshot);
        try
        {
            // EF's SaveChanges transaction covers the snapshot insert and revision-checked terminal update together.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException ||
            error.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            db.ChangeTracker.Clear();
            var current = await Owned(db, user, id).AsNoTracking().SingleOrDefaultAsync(ct);
            return current is null ? Results.NotFound() : await ReplayAsync(current, body.ExpectedRevision, db, ct);
        }
        return Results.Created($"/api/instances/{snapshot.Id}", SnapshotPreview.From(snapshot));
    }

    private static async Task<IResult> ReplayAsync(ActivityDraft draft, long expected, LearningDbContext db, CancellationToken ct)
    {
        if (draft.ReleasedSourceRevision != expected) return Conflict();
        var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == draft.ReleasedSnapshotId && s.FamilyId == draft.FamilyId, ct);
        return snapshot is null ? Results.Problem(statusCode: 410, title: "הפעילות המוכנה נמחקה.") : Results.Ok(SnapshotPreview.From(snapshot));
    }

    private static IQueryable<ActivityDraft> Owned(LearningDbContext db, ClaimsPrincipal user, Guid id) =>
        db.ActivityDrafts.Where(d => d.Id == id && d.FamilyId == user.FamilyId());

    // JsonElement retains omission versus null; native strict deserialization also rejects quoted numbers and unknown members.
    private static T Read<T>(JsonElement value)
    {
        try
        {
            return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? throw new JsonException() : value.Deserialize<T>(EngineJson.Options) ?? throw new JsonException();
        }
        catch (JsonException) { throw new TaskValidationException("request", "הבקשה אינה במבנה נתמך."); }
    }

    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "הטיוטה השתנתה או אינה זמינה לעריכה. יש לטעון את המצב השמור.");
}
