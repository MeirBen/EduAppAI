using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Read/delete only parent snapshot routes in the staged content-first composition.</summary>
public static class SnapshotEndpoints
{
    /// <summary>Maps immutable snapshot previews and deletion on the shared authorized parent API group.</summary>
    public static void MapSnapshotEndpoints(this RouteGroupBuilder api)
    {
        var snapshots = api.MapGroup("/instances");
        snapshots.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == user.FamilyId()).OrderByDescending(s => s.ReviewedAtUtc)
                .Take(100).Select(s => new InstanceSummary(s.Id, s.Title, "Ready", s.ReviewedAtUtc)).ToListAsync(ct));
        snapshots.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.FamilyId == user.FamilyId(), ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(SnapshotPreview.From(snapshot));
        });
        snapshots.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskSnapshots.Where(s => s.Id == id && s.FamilyId == user.FamilyId()).ExecuteDeleteAsync(ct) == 0
                ? Results.NotFound() : Results.NoContent());
    }
}
