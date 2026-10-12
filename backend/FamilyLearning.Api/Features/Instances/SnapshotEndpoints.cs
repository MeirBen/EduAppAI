using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>
/// Parent snapshot reads, library removal and the archive. Removing assigned content archives it unchanged; only
/// deleting it from the archive erases it, together with its assignments and their work.
/// </summary>
public static class SnapshotEndpoints
{
    public static void MapSnapshotEndpoints(this RouteGroupBuilder api)
    {
        var snapshots = api.MapGroup("/instances");
        snapshots.MapGet("/", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct, int page = 1, int pageSize = 25) =>
            ListAsync(user, db, archived: false, new PageRequest(page, pageSize), ct));
        snapshots.MapGet("/archive", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct, int page = 1, int pageSize = 25) =>
            ListAsync(user, db, archived: true, new PageRequest(page, pageSize), ct));
        snapshots.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.FamilyId == user.FamilyId(), ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(SnapshotPreview.From(snapshot));
        });
        snapshots.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var familyId = user.FamilyId();
            var snapshots = db.TaskSnapshots.Where(s => s.Id == id && s.FamilyId == familyId);
            if (!await snapshots.AnyAsync(ct)) return Results.NotFound();
            if (await db.Assignments.AnyAsync(a => a.FamilyId == familyId && a.SnapshotId == id, ct))
            {
                var now = clock.GetUtcNow().UtcDateTime;
                await snapshots.Where(s => s.ArchivedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ArchivedAtUtc, now), ct);
            }
            else await snapshots.ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
        snapshots.MapDelete("/archive/{id:guid}", DeleteArchivedAsync);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, LearningDbContext db, bool archived, PageRequest paging, CancellationToken ct)
    {
        if (!paging.IsValid) return PageRequest.Invalid();
        var snapshots = db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == user.FamilyId());
        snapshots = archived ? snapshots.Where(s => s.ArchivedAtUtc != null) : snapshots.Where(s => s.ArchivedAtUtc == null);
        return Results.Ok(await paging.ReadAsync(snapshots.OrderByDescending(s => s.ReviewedAtUtc).ThenByDescending(s => s.Id)
            .Select(s => new SnapshotSummary(s.Id, s.Title, s.ReviewedAtUtc, s.ArchivedAtUtc,
                db.Assignments.Any(a => a.FamilyId == s.FamilyId && a.SnapshotId == s.Id))), ct));
    }

    /// <summary>
    /// Permanently deletes an archived snapshot with its assignments and sessions in one transaction. Content still in
    /// the library is not in the archive (404), so removing it twice can never erase children's work.
    /// </summary>
    private static async Task<IResult> DeleteArchivedAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var familyId = user.FamilyId();
        var snapshot = db.TaskSnapshots.Where(s => s.Id == id && s.FamilyId == familyId && s.ArchivedAtUtc != null);
        if (!await snapshot.AnyAsync(ct)) return Results.NotFound();
        await db.TaskSessions.Where(s => s.Assignment.FamilyId == familyId && s.Assignment.SnapshotId == id).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.FamilyId == familyId && a.SnapshotId == id).ExecuteDeleteAsync(ct);
        await snapshot.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
