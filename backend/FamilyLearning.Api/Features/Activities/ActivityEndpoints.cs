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

/// <summary>Owned draft checkpoints and immutable release. No endpoint in this feature calls a provider.</summary>
public static class ActivityEndpoints
{
    /// <summary>Maps parent-only activity checkpoints, adoption and release on the shared API group.</summary>
    public static void MapActivityEndpoints(this RouteGroupBuilder api)
    {
        var drafts = api.MapGroup("/activity-drafts");
        drafts.MapPost("/", CreateAsync);
        drafts.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.ActivityDrafts.AsNoTracking().Where(d => d.FamilyId == user.FamilyId())
                .OrderByDescending(d => d.UpdatedAtUtc).Take(100)
                .Select(d => new ActivitySummary(d.Id, d.Name, d.Revision, d.ReleasedSnapshotId != null, d.UpdatedAtUtc)).ToListAsync(ct));
        drafts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var draft = await Owned(db, user, id).AsNoTracking().SingleOrDefaultAsync(ct);
            return draft is null ? Results.NotFound() : Results.Ok(ActivityDetail.From(draft));
        });
        drafts.MapPut("/{id:guid}", SaveAsync);
        drafts.MapPost("/{id:guid}/adopt-content", AdoptAsync);
        drafts.MapPost("/{id:guid}/release", ReleaseAsync);
        drafts.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await Owned(db, user, id).ExecuteDeleteAsync(ct) == 0 ? Results.NotFound() : Results.NoContent());
    }

    private static async Task<IResult> CreateAsync(CreateActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        LearningPlan plan;
        TaskRequest input;
        TaskDocument document;
        Guid? templateVersionId = null;
        Guid? sourceSnapshotId = null;
        if (body.SnapshotId.ValueKind != JsonValueKind.Undefined)
        {
            if (new[] { body.Plan, body.Input, body.TemplateId, body.ExpectedVersion }.Any(p => p.ValueKind != JsonValueKind.Undefined))
                return Invalid("snapshotId", "יש לבחור מקור אחד ליצירת הטיוטה.");
            var snapshotId = Read<Guid>(body.SnapshotId);
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.FamilyId == user.FamilyId(), ct);
            if (snapshot is null) return Results.NotFound();
            plan = StoredJson.Read<LearningPlan>(snapshot.PlanJson);
            input = StoredJson.Read<TaskRequest>(snapshot.InputJson);
            document = StoredJson.Read<TaskDocument>(snapshot.DocumentJson);
            templateVersionId = snapshot.TemplateVersionId;
            sourceSnapshotId = snapshot.Id;
        }
        else
        {
            if (body.TemplateId.ValueKind != JsonValueKind.Undefined)
            {
                var templateId = Read<Guid>(body.TemplateId);
                var version = await (from template in db.TaskTemplates
                                     join revision in db.TaskTemplateVersions on template.Id equals revision.TemplateId
                                     where template.Id == templateId && template.FamilyId == user.FamilyId() && revision.Version == template.CurrentVersion
                                     select revision).AsNoTracking().SingleOrDefaultAsync(ct);
                if (version is null) return Results.NotFound();
                if (Read<int>(body.ExpectedVersion) != version.Version) return Conflict();
                templateVersionId = version.Id;
                plan = body.Plan.ValueKind == JsonValueKind.Undefined ? StoredJson.Read<LearningPlan>(version.DefinitionJson) : Read<LearningPlan>(body.Plan);
            }
            else
            {
                if (body.ExpectedVersion.ValueKind != JsonValueKind.Undefined) return Invalid("expectedVersion", "יש לציין תבנית מקור.");
                plan = Read<LearningPlan>(body.Plan);
            }
            input = Read<TaskRequest>(body.Input);
            document = TaskAssembly.CreateDocument(Resolve(plan, input));
        }
        // Copying a snapshot keeps content/provenance, but never copies its parent review or terminal state.
        var resolved = Resolve(plan, input);
        var errors = TaskDocumentValidator.ValidateDraft(resolved, document).Errors;
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var draft = new ActivityDraft(user.FamilyId(), plan.Name, StoredJson.Write(plan), StoredJson.Write(input),
            StoredJson.Write(document), templateVersionId, sourceSnapshotId, user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        db.ActivityDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/activity-drafts/{draft.Id}", ActivityDetail.From(draft));
    }

    private static async Task<IResult> SaveAsync(Guid id, SaveActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var draft = await Owned(db, user, id).SingleOrDefaultAsync(ct);
        if (draft is null) return Results.NotFound();
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (body.Input is null) return Invalid("input", "יש לציין קלט לפעילות.");
        var before = Resolve(StoredJson.Read<LearningPlan>(draft.PlanJson), StoredJson.Read<TaskRequest>(draft.InputJson));
        var request = Resolve(body.Plan, body.Input);
        var document = ActivityDraftChanges.Apply(before, request, StoredJson.Read<TaskDocument>(draft.DocumentJson), body.Document);
        draft.Save(body.Plan.Name, StoredJson.Write(body.Plan), StoredJson.Write(body.Input), StoredJson.Write(document));
        return await SaveCheckpointAsync(draft, db, user, ct);
    }

    private static async Task<IResult> AdoptAsync(Guid id, AdoptActivityRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var draft = await Owned(db, user, id).SingleOrDefaultAsync(ct);
        if (draft is null) return Results.NotFound();
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (body.MaterialIds is not { Length: <= 4 } || body.QuestionIds is not { Length: <= EngineValidation.QuestionLimit } ||
            body.MaterialIds.Length + body.QuestionIds.Length == 0)
            return Invalid("selection", "יש לבחור תוכן לבדיקה ולאימוץ.");
        var request = Resolve(StoredJson.Read<LearningPlan>(draft.PlanJson), StoredJson.Read<TaskRequest>(draft.InputJson));
        var document = TaskAssembly.Adopt(request, StoredJson.Read<TaskDocument>(draft.DocumentJson), body.MaterialIds, body.QuestionIds, DateTime.UtcNow);
        draft.Save(draft.Name, draft.PlanJson, draft.InputJson, StoredJson.Write(document));
        return await SaveCheckpointAsync(draft, db, user, ct);
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
        var input = Resolve(StoredJson.Read<LearningPlan>(draft.PlanJson), StoredJson.Read<TaskRequest>(draft.InputJson));
        var document = StoredJson.Read<TaskDocument>(draft.DocumentJson);
        var errors = TaskDocumentValidator.ValidateRelease(input, document);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var reviewedAt = DateTime.UtcNow;
        var snapshot = new TaskSnapshot(draft.FamilyId, draft.Id, draft.Revision, document.Title, draft.PlanJson, draft.InputJson,
            StoredJson.Write(input), draft.DocumentJson, StoredJson.Write(TextLength.Measure(input, document)), EngineVersions.Revision,
            draft.TemplateVersionId, draft.SourceSnapshotId, draft.CreatedByParentId, draft.CreatedAtUtc,
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

    private static ResolvedTaskRequest Resolve(LearningPlan plan, TaskRequest input)
    {
        var result = TaskRequestResolver.Resolve(plan, input);
        return result.Value ?? throw new TaskValidationException(result.Errors);
    }

    // JsonElement retains omission versus null; native strict deserialization also rejects quoted numbers and unknown members.
    private static T Read<T>(JsonElement value)
    {
        try
        {
            return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? throw new JsonException() : value.Deserialize<T>(EngineJson.Options) ?? throw new JsonException();
        }
        catch (JsonException) { throw new TaskValidationException(new Dictionary<string, string[]> { ["request"] = ["הבקשה אינה במבנה נתמך."] }); }
    }

    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "הטיוטה השתנתה או אינה זמינה לעריכה. יש לטעון את המצב השמור.");
}
