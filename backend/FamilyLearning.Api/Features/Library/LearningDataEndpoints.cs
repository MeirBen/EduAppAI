using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Library;

/// <summary>Explicit family learning reset; parent accounts, family identity and AI configuration are retained.</summary>
public static class LearningDataEndpoints
{
    public static void MapLearningDataEndpoints(this RouteGroupBuilder api) => api.MapDelete("/learning-data",
        async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var familyId = user.FamilyId();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.TaskSessions.Where(s => s.Assignment.FamilyId == familyId).ExecuteDeleteAsync(ct);
            await db.Assignments.Where(a => a.FamilyId == familyId).ExecuteDeleteAsync(ct);
            var children = db.Children.Where(c => c.FamilyId == familyId).Select(c => c.Id);
            await db.ChildDeviceGrants.Where(g => children.Contains(g.ChildId)).ExecuteDeleteAsync(ct);
            await db.ChildActivations.Where(a => children.Contains(a.ChildId)).ExecuteDeleteAsync(ct);
            await db.Children.Where(c => c.FamilyId == familyId).ExecuteDeleteAsync(ct);
            await db.ActivityDrafts.Where(d => d.FamilyId == familyId).ExecuteDeleteAsync(ct);
            await db.TaskSnapshots.Where(s => s.FamilyId == familyId).ExecuteDeleteAsync(ct);
            var templates = db.TaskTemplates.Where(t => t.FamilyId == familyId);
            await db.TaskTemplateVersions.Where(v => templates.Select(t => t.Id).Contains(v.TemplateId)).ExecuteDeleteAsync(ct);
            await templates.ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
}
