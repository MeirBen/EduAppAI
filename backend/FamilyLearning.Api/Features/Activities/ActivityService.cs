using System.Text.Json;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Family-owned draft checkpoints and immutable release, with persistence decisions kept together.</summary>
internal sealed class ActivityService(LearningDbContext db, TimeProvider clock)
{
    internal async Task<FeatureResult<ActivityDetail>> FindAsync(Guid familyId, Guid id, CancellationToken ct)
    {
        var draft = await Owned(familyId, id).AsNoTracking().SingleOrDefaultAsync(ct);
        return draft is null ? FeatureFailure.NotFound : ActivityDetail.From(draft);
    }

    internal async Task<FeatureResult<bool>> DeleteAsync(Guid familyId, Guid id, CancellationToken ct) =>
        await Owned(familyId, id).ExecuteDeleteAsync(ct) == 0 ? FeatureFailure.NotFound : true;

    internal async Task<FeatureResult<PageResponse<ActivitySummary>>> ListAsync(Guid familyId, PageRequest paging, CancellationToken ct)
    {
        if (!paging.IsValid) return PageRequest.Invalid();
        return await paging.ReadAsync(db.ActivityDrafts.AsNoTracking().Where(d => d.FamilyId == familyId && d.ReleasedSnapshotId == null)
            .OrderByDescending(d => d.UpdatedAtUtc).ThenByDescending(d => d.Id)
            .Select(d => new ActivitySummary(d.Id, d.Name, d.Revision, d.UpdatedAtUtc)), ct);
    }

    /// <summary>
    /// Creates the draft the client named. A retry after a lost response replays that draft while it still holds the same
    /// plan and source snapshot; any other use of the identity conflicts, so no request ever adds a second draft.
    /// </summary>
    internal async Task<FeatureResult<(ActivityDetail Value, bool Created)>> CreateAsync(Guid familyId, string parentId, CreateActivityRequest body, CancellationToken ct)
    {
        if (body.Id == Guid.Empty) return FeatureFailure.Invalid("id", "יש לציין מזהה לטיוטה החדשה.");
        LearningPlan plan;
        TaskDocument? document = null;
        Guid? sourceSnapshotId = null;
        if (body.SnapshotId.ValueKind != JsonValueKind.Undefined)
        {
            if (body.Chat is not null || body.Plan.ValueKind != JsonValueKind.Undefined)
                return FeatureFailure.Invalid("snapshotId", "יש לבחור מקור אחד ליצירת הטיוטה.");
            var snapshotId = Read<Guid>(body.SnapshotId);
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.FamilyId == familyId, ct);
            if (snapshot is null) return FeatureFailure.NotFound;
            plan = StoredJson.Read<LearningPlan>(snapshot.PlanJson);
            document = StoredJson.Read<TaskDocument>(snapshot.DocumentJson);
            sourceSnapshotId = snapshot.Id;
        }
        else plan = Read<LearningPlan>(body.Plan);
        // Copying a snapshot keeps content/provenance, but never copies its parent review or terminal state.
        var resolved = TaskRequestResolver.ResolveOrThrow(plan);
        document ??= TaskAssembly.CreateDocument(resolved);
        var errors = TaskDocumentValidator.ValidateDraft(resolved, document).Errors;
        if (errors.Count > 0) return FeatureFailure.Invalid(errors);
        var planJson = StoredJson.Write(plan);
        var draft = new ActivityDraft(body.Id, familyId, ActivityDraft.LibraryName(plan, document), planJson,
            StoredJson.Write(document), sourceSnapshotId, parentId);
        // A replay still validates all submitted chat before checking the persisted identity.
        draft.ImportChat(body.Chat);
        // SQLite's immediate write transaction makes the identity lookup and the insertion one decision.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.ActivityDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == body.Id, ct) is { } existing)
            return existing.FamilyId == familyId && existing.PlanJson == planJson && existing.SourceSnapshotId == sourceSnapshotId
                ? (ActivityDetail.From(existing), false) : Conflict();
        db.ActivityDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (ActivityDetail.From(draft), true);
    }

    internal async Task<FeatureResult<ActivityDetail>> SaveAsync(Guid familyId, Guid id, SaveActivityRequest body, CancellationToken ct)
    {
        var draft = await Owned(familyId, id).SingleOrDefaultAsync(ct);
        if (draft is null) return FeatureFailure.NotFound;
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        var (plan, document) = ActivityDraftChanges.Apply(draft, body);
        draft.Save(plan, document);
        return await SaveCheckpointAsync(draft, familyId, ct);
    }

    internal async Task<FeatureResult<ActivityDetail>> AdoptAsync(Guid familyId, Guid id, AdoptActivityRequest body, CancellationToken ct)
    {
        var draft = await Owned(familyId, id).SingleOrDefaultAsync(ct);
        if (draft is null) return FeatureFailure.NotFound;
        if (draft.ReleasedSnapshotId.HasValue || draft.Revision != body.ExpectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        if (body.MaterialIds is not { Length: <= EngineValidation.MaxMaterials } || body.QuestionIds is not { Length: <= EngineValidation.MaxQuestionCount } ||
            body.MaterialIds.Length + body.QuestionIds.Length == 0)
            return FeatureFailure.Invalid("selection", "יש לבחור תוכן לבדיקה ולאימוץ.");
        var request = TaskRequestResolver.ResolveOrThrow(draft.Plan);
        var document = TaskAssembly.Adopt(request, draft.Document, body.MaterialIds, body.QuestionIds, DateTime.UtcNow);
        draft.Save(draft.Plan, document);
        return await SaveCheckpointAsync(draft, familyId, ct);
    }

    internal async Task<FeatureResult<ActivityDetail>> UndoAsync(Guid familyId, Guid id, UndoActivityRequest body, CancellationToken ct)
    {
        // Reply-only operations can leave the revision unchanged; serialize reading and extending the chat with their writes.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var draft = await Owned(familyId, id).SingleOrDefaultAsync(ct);
        if (draft is null) return FeatureFailure.NotFound;
        if (draft.Revision != body.ExpectedRevision || !draft.CanUndo) return Conflict();
        draft.RestoreUndo(clock.GetUtcNow().UtcDateTime);
        var result = await SaveCheckpointAsync(draft, familyId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<FeatureResult<ActivityDetail>> SaveCheckpointAsync(ActivityDraft draft, Guid familyId, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return await Owned(familyId, draft.Id).AsNoTracking().AnyAsync(ct) ? Conflict() : FeatureFailure.NotFound;
        }
        return ActivityDetail.From(draft);
    }

    internal async Task<FeatureResult<(SnapshotPreview Value, bool Created)>> ReleaseAsync(Guid familyId, string parentId,
        Guid id, long expectedRevision, CancellationToken ct)
    {
        var drafts = db.ActivityDrafts.Where(d => d.Id == id && d.FamilyId == familyId);
        var draft = await drafts.SingleOrDefaultAsync(ct);
        if (draft is null) return FeatureFailure.NotFound;
        if (draft.ReleasedSnapshotId.HasValue) return await ReplayAsync(draft, expectedRevision, ct);
        if (draft.Revision != expectedRevision || draft.ActiveOperationId.HasValue) return Conflict();
        var input = TaskRequestResolver.ResolveOrThrow(draft.Plan);
        var document = draft.Document;
        var errors = TaskDocumentValidator.ValidateRelease(input, document);
        if (errors.Count > 0) return FeatureFailure.Invalid(errors);
        var reviewedAt = DateTime.UtcNow;
        var snapshot = new TaskSnapshot(draft.FamilyId, draft.Id, draft.Revision, document.Title, draft.PlanJson, draft.DocumentJson,
            StoredJson.Write(TextLength.Measure(input, document)), EngineVersions.Revision,
            draft.SourceSnapshotId, draft.CreatedByParentId, draft.CreatedAtUtc, parentId, reviewedAt);
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
            var current = await drafts.AsNoTracking().SingleOrDefaultAsync(ct);
            return current is null ? FeatureFailure.NotFound : await ReplayAsync(current, expectedRevision, ct);
        }
        return (await SnapshotReader.ReadAsync(db, snapshot, ct), true);
    }

    private async Task<FeatureResult<(SnapshotPreview Value, bool Created)>> ReplayAsync(ActivityDraft draft, long expectedRevision, CancellationToken ct)
    {
        if (draft.ReleasedSourceRevision != expectedRevision) return Conflict();
        var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == draft.ReleasedSnapshotId && s.FamilyId == draft.FamilyId, ct);
        return snapshot is null ? FeatureFailure.Gone("הפעילות המוכנה נמחקה.") : (await SnapshotReader.ReadAsync(db, snapshot, ct), false);
    }

    private IQueryable<ActivityDraft> Owned(Guid familyId, Guid id) =>
        db.ActivityDrafts.Where(d => d.Id == id && d.FamilyId == familyId);

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

    private static FeatureFailure Conflict() => FeatureFailure.Conflict("הטיוטה השתנתה או אינה זמינה לעריכה. יש לטעון את המצב השמור.");
}
