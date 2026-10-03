using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Templates;

/// <summary>Canonical plan publication; never modifies copied activities.</summary>
public static class PlanTemplateEndpoints
{
    public static void MapPlanTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/templates");
        templates.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskTemplates.AsNoTracking().Where(t => t.FamilyId == user.FamilyId()).OrderByDescending(t => t.CreatedAtUtc)
                .Take(100).Select(t => new TemplateSummary(t.Id, t.Name, t.CurrentVersion, t.CreatedAtUtc)).ToListAsync(ct));
        templates.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var version = await (from template in db.TaskTemplates
                                 join revision in db.TaskTemplateVersions on template.Id equals revision.TemplateId
                                 where template.Id == id && template.FamilyId == user.FamilyId() && revision.Version == template.CurrentVersion
                                 select revision).AsNoTracking().SingleOrDefaultAsync(ct);
            return version is null ? Results.NotFound() : Results.Ok(PlanTemplateDetail.From(version));
        });
        templates.MapPost("/", async (LearningPlan plan, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var errors = LearningPlanValidator.Validate(plan);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var template = new TaskTemplate(user.FamilyId(), plan.Name);
            var version = new TaskTemplateVersion(template.Id, 1, StoredJson.Write(plan));
            db.AddRange(template, version);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/templates/{template.Id}", PlanTemplateDetail.From(version));
        });
        templates.MapPost("/{id:guid}/versions", PublishAsync);
        templates.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) => DeleteAsync(id, user, db, ct));
        templates.MapDelete("/", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) => DeleteAsync(null, user, db, ct));
    }

    private static async Task<IResult> PublishAsync(Guid id, PublishPlanRequest body, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var template = await db.TaskTemplates.SingleOrDefaultAsync(t => t.Id == id && t.FamilyId == user.FamilyId(), ct);
        if (template is null) return Results.NotFound();
        if (template.CurrentVersion != body.ExpectedVersion) return Conflict();
        var errors = LearningPlanValidator.Validate(body.Definition);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        template.PublishNextVersion(body.Definition.Name);
        var version = new TaskTemplateVersion(id, template.CurrentVersion, StoredJson.Write(body.Definition));
        db.TaskTemplateVersions.Add(version);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 787 }) { return Results.NotFound(); }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 }) { return Conflict(); }
        return Results.Created($"/api/templates/{id}", PlanTemplateDetail.From(version));
    }

    private static async Task<IResult> DeleteAsync(Guid? id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var familyId = user.FamilyId();
        var templates = db.TaskTemplates.Where(t => t.FamilyId == familyId);
        if (id.HasValue) templates = templates.Where(t => t.Id == id.Value);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (id.HasValue && !await templates.AnyAsync(ct)) return Results.NotFound();
        if (!id.HasValue)
        {
            await db.ActivityDrafts.Where(d => d.FamilyId == familyId).ExecuteDeleteAsync(ct);
            await db.TaskSnapshots.Where(s => s.FamilyId == familyId).ExecuteDeleteAsync(ct);
        }
        // Template provenance is detached: deleting a template alone must leave drafts and snapshots intact.
        await db.TaskTemplateVersions.Where(v => templates.Select(t => t.Id).Contains(v.TemplateId)).ExecuteDeleteAsync(ct);
        await templates.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "התבנית השתנתה. יש לטעון אותה לפני פרסום גרסה נוספת.");
}
