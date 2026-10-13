using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Shared library list/count rules, composed into SQL before paging or aggregation.</summary>
internal static class SnapshotQueries
{
    /// <summary>A family's open (assigned or awaiting review) and finished work; withdrawn work is in neither.</summary>
    internal static (IQueryable<Assignment> Open, IQueryable<Assignment> Finished) Work(LearningDbContext db, Guid familyId)
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
}
