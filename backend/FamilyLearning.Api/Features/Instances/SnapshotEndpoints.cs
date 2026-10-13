using System.Security.Claims;
using FamilyLearning.Api.Features.Assignments;
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
        snapshots.MapGet("/", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct, string? status = null, int page = 1, int pageSize = PageRequest.DefaultSize) =>
            ListAsync(user, db, archived: false, status, new PageRequest(page, pageSize), ct));
        snapshots.MapGet("/archive", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct, int page = 1, int pageSize = PageRequest.DefaultSize) =>
            ListAsync(user, db, archived: true, null, new PageRequest(page, pageSize), ct));
        snapshots.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.FamilyId == user.FamilyId(), ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(await SnapshotPreview.ReadAsync(db, snapshot, ct));
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

    /// <summary>A family's open (assigned or awaiting review) and finished work; withdrawn work is in neither.</summary>
    private static (IQueryable<Assignment> Open, IQueryable<Assignment> Finished) Work(LearningDbContext db, Guid familyId)
    {
        var assignments = db.Assignments.AsNoTracking().Where(a => a.FamilyId == familyId);
        return (assignments.Where(a => a.Status == "assigned" || a.Status == "awaiting-review"),
            assignments.Where(a => a.Status == "completed"));
    }

    /// <summary>
    /// Keeps one library view. Every snapshot is in exactly one: <c>assigned</c> while any work is open, else
    /// <c>completed</c> once any is finished, else <c>unassigned</c>.
    /// </summary>
    internal static IQueryable<TaskSnapshot> InView(this IQueryable<TaskSnapshot> snapshots, LearningDbContext db, Guid familyId,
        string view)
    {
        var (open, finished) = Work(db, familyId);
        return view switch
        {
            "assigned" => snapshots.Where(s => open.Any(a => a.SnapshotId == s.Id)),
            "completed" => snapshots.Where(s => !open.Any(a => a.SnapshotId == s.Id) && finished.Any(a => a.SnapshotId == s.Id)),
            _ => snapshots.Where(s => !open.Any(a => a.SnapshotId == s.Id) && !finished.Any(a => a.SnapshotId == s.Id)),
        };
    }

    /// <summary>
    /// Lists one page, in one view when <paramref name="status"/> names it, newest first by the date each row shows: when
    /// it was archived, else its latest open assignment, else its latest completion, else its review.
    /// </summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, LearningDbContext db, bool archived, string? status,
        PageRequest paging, CancellationToken ct)
    {
        if (!paging.IsValid) return PageRequest.Invalid();
        if (status is not (null or "assigned" or "unassigned" or "completed"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["מצב הפעילות אינו תקף."] });
        var familyId = user.FamilyId();
        var snapshots = db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == familyId);
        snapshots = archived ? snapshots.Where(s => s.ArchivedAtUtc != null) : snapshots.Where(s => s.ArchivedAtUtc == null);
        if (status is not null) snapshots = snapshots.InView(db, familyId, status);
        var (open, finished) = Work(db, familyId);
        var rows = snapshots.Select(s => new
        {
            Snapshot = s,
            AssignedAtUtc = open.Where(a => a.SnapshotId == s.Id).Max(a => (DateTime?)a.CreatedAtUtc),
            CompletedAtUtc = finished.Where(a => a.SnapshotId == s.Id).Max(a => a.Session!.ReviewedAtUtc ?? a.Session!.SubmittedAtUtc),
        });
        return Results.Ok(await paging.ReadAsync(rows
            .OrderByDescending(r => r.Snapshot.ArchivedAtUtc ?? r.AssignedAtUtc ?? r.CompletedAtUtc ?? r.Snapshot.ReviewedAtUtc)
            .ThenByDescending(r => r.Snapshot.Id)
            .Select(r => new SnapshotSummary(r.Snapshot.Id, r.Snapshot.Title, r.Snapshot.ReviewedAtUtc, r.AssignedAtUtc,
                r.CompletedAtUtc, r.Snapshot.ArchivedAtUtc,
                db.Assignments.Where(a => a.FamilyId == familyId && a.SnapshotId == r.Snapshot.Id && a.Status != "withdrawn")
                    .OrderBy(a => a.Child.Name).ThenBy(a => a.Id)
                    .Select(a => new SnapshotChild(a.Child.Name, a.Status,
                        LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.finalTotal"),
                        LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.possibleTotal")))
                    .ToArray())), ct));
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
