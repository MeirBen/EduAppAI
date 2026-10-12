using System.Security.Claims;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Library;

/// <summary>Exact sizes of the family's library sections, read in one query, so folded sections can show their counts.</summary>
public static class LibraryCountsEndpoints
{
    public static void MapLibraryCountsEndpoints(this RouteGroupBuilder api) => api.MapGet("/library/counts",
        async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var familyId = user.FamilyId();
            var ready = db.TaskSnapshots.Where(s => s.FamilyId == familyId && s.ArchivedAtUtc == null);
            var (assigned, unassigned, completed) =
                (ready.InView(db, familyId, "assigned"), ready.InView(db, familyId, "unassigned"), ready.InView(db, familyId, "completed"));
            return Results.Ok(await db.Families.AsNoTracking().Where(f => f.Id == familyId).Select(f => new LibraryCounts(
                db.ActivityDrafts.Count(d => d.FamilyId == f.Id && d.ReleasedSnapshotId == null),
                assigned.Count(), unassigned.Count(), completed.Count(),
                db.TaskSnapshots.Count(s => s.FamilyId == f.Id && s.ArchivedAtUtc != null))).SingleAsync(ct));
        });
}

/// <summary>Unreleased drafts, ready snapshots in each view, and archived snapshots.</summary>
public sealed record LibraryCounts(int Drafts, int Assigned, int Unassigned, int Completed, int Archived);
