using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Templates;

/// <summary>Parent operations for family-owned templates and immutable published revisions.</summary>
public static class TemplateEndpoints
{
    /// <summary>Maps authenticated template routes onto the API group configured with CSRF protection.</summary>
    public static void MapTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/templates");
        templates.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskTemplates.AsNoTracking().Where(t => t.FamilyId == user.FamilyId())
                .OrderByDescending(t => t.CreatedAtUtc).Take(100)
                .Select(t => new TemplateSummary(t.Id, t.Name, t.CurrentVersion, t.CreatedAtUtc)).ToListAsync(ct));
        templates.MapGet("/{id:guid}", GetAsync);
        templates.MapPost("/", CreateAsync);
        templates.MapPost("/{id:guid}/versions", CreateVersionAsync);
        templates.MapDelete("/{id:guid}", (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            DeleteAsync(id, user, db, ct));
        templates.MapDelete("/", (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            DeleteAsync(null, user, db, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        // One ownership-scoped lookup gives missing and foreign IDs the same 404 response.
        var template = await db.TaskTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.FamilyId == user.FamilyId(), ct);
        if (template is null) return Results.NotFound();
        var version = await db.TaskTemplateVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.TemplateId == id && v.Version == template.CurrentVersion, ct);
        if (version is null) return Results.NotFound();
        return Results.Ok(TemplateDetail.From(version));
    }

    /// <summary>Deletes one template or the family's whole library, including all revisions and saved tasks.</summary>
    private static async Task<IResult> DeleteAsync(Guid? id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var familyId = user.FamilyId();
        var templates = db.TaskTemplates.Where(t => t.FamilyId == familyId);
        if (id.HasValue) templates = templates.Where(t => t.Id == id.Value);
        var versions = db.TaskTemplateVersions.Where(v => templates.Select(t => t.Id).Contains(v.TemplateId));
        // Bulk deletes bypass SaveChanges: one transaction keeps dependent rows and revisions atomic.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (id.HasValue && !await templates.AnyAsync(ct)) return Results.NotFound();
        await db.TaskInstances.Where(i => i.FamilyId == familyId && versions.Select(v => v.Id).Contains(i.TemplateVersionId))
            .ExecuteDeleteAsync(ct);
        await versions.ExecuteDeleteAsync(ct);
        await templates.ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateAsync(TaskTemplateDefinition definition, ClaimsPrincipal user,
        LearningDbContext db, CancellationToken ct)
    {
        var errors = TemplateValidator.Validate(definition);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        definition = definition with { Name = definition.Name.Trim() };
        var template = new TaskTemplate(user.FamilyId(), definition.Name);
        var version = new TaskTemplateVersion(template.Id, 1, StoredJson.Write(definition));
        db.TaskTemplates.Add(template);
        db.TaskTemplateVersions.Add(version);
        // One SaveChanges transaction prevents a template from existing without its first revision.
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/templates/{template.Id}", TemplateDetail.From(version));
    }

    private static async Task<IResult> CreateVersionAsync(Guid id, CreateVersionRequest request,
        ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var template = await db.TaskTemplates.SingleOrDefaultAsync(t => t.Id == id && t.FamilyId == user.FamilyId(), ct);
        if (template is null) return Results.NotFound();
        if (template.CurrentVersion != request.ExpectedVersion) return VersionConflict();
        var errors = TemplateValidator.Validate(request.Definition);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var definition = request.Definition with { Name = request.Definition.Name.Trim() };
        template.PublishNextVersion(definition.Name);
        var version = new TaskTemplateVersion(id, template.CurrentVersion, StoredJson.Write(definition));
        db.TaskTemplateVersions.Add(version);
        try
        {
            // Persist the pointer and snapshot atomically; EF also checks the concurrency token.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return VersionConflict(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            // Deletion can win after the owned read; EF inserts the revision before updating the pointer.
            return Results.NotFound();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            // The (TemplateId, Version) unique constraint also protects simultaneous publication.
            return VersionConflict();
        }
        return Results.Created($"/api/templates/{id}", TemplateDetail.From(version));
    }

    private static IResult VersionConflict() => Results.Problem(statusCode: 409,
        title: "התבנית השתנתה. יש לרענן את העמוד לפני שמירת גרסה נוספת.");
}
