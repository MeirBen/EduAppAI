using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Parent snapshot reads and library removal; assigned content is archived and remains immutable.</summary>
public static class SnapshotEndpoints
{
    public static void MapSnapshotEndpoints(this RouteGroupBuilder api)
    {
        var snapshots = api.MapGroup("/instances");
        snapshots.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == user.FamilyId() && s.ArchivedAtUtc == null).OrderByDescending(s => s.ReviewedAtUtc)
                .Take(EngineValidation.ListLimit).Select(s => new SnapshotSummary(s.Id, s.Title, "Ready", s.ReviewedAtUtc, db.Assignments.Any(a => a.FamilyId == s.FamilyId && a.SnapshotId == s.Id))).ToListAsync(ct));
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
    }
}
