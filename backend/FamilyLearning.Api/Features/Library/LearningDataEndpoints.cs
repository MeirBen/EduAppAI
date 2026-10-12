using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Library;

/// <summary>
/// Explicit family data deletions, each in one transaction: all activities, or all children's data. Parent accounts,
/// family identity and AI configuration are always retained.
/// </summary>
public static class LearningDataEndpoints
{
    public static void MapLearningDataEndpoints(this RouteGroupBuilder api)
    {
        var data = api.MapGroup("/learning-data");
        data.MapDelete("/activities", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            DeleteAsync(db, user.FamilyId(), ct, async familyId =>
            {
                await db.ActivityDrafts.Where(d => d.FamilyId == familyId).ExecuteDeleteAsync(ct);
                await db.TaskSnapshots.Where(s => s.FamilyId == familyId).ExecuteDeleteAsync(ct);
            }));
        data.MapDelete("/children", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            DeleteAsync(db, user.FamilyId(), ct, async familyId =>
            {
                var children = db.Children.Where(c => c.FamilyId == familyId).Select(c => c.Id);
                await db.ChildDeviceGrants.Where(g => children.Contains(g.ChildId)).ExecuteDeleteAsync(ct);
                await db.ChildActivations.Where(a => children.Contains(a.ChildId)).ExecuteDeleteAsync(ct);
                await db.Children.Where(c => c.FamilyId == familyId).ExecuteDeleteAsync(ct);
                // The archive keeps removed activities only for the work deleted here.
                await db.TaskSnapshots.Where(s => s.FamilyId == familyId && s.ArchivedAtUtc != null).ExecuteDeleteAsync(ct);
            }));
    }

    /// <summary>Assigned work belongs to both an activity and a child, so either deletion removes it first.</summary>
    private static async Task<IResult> DeleteAsync(LearningDbContext db, Guid familyId, CancellationToken ct,
        Func<Guid, Task> deleteRest)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.TaskSessions.Where(s => s.Assignment.FamilyId == familyId).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.FamilyId == familyId).ExecuteDeleteAsync(ct);
        await deleteRest(familyId);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
}
