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
        var templates = api.MapGroup("/templates").RequireAuthorization("Parent");
        templates.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskTemplates.AsNoTracking().Where(t => t.FamilyId == user.FamilyId())
                .OrderByDescending(t => t.CreatedAtUtc).Take(100)
                .Select(t => new TemplateSummary(t.Id, t.Name, t.CurrentVersion, t.CreatedAtUtc)).ToListAsync(ct));
        templates.MapGet("/{id:guid}", GetAsync);
        templates.MapPost("/", CreateAsync);
        templates.MapPost("/{id:guid}/versions", CreateVersionAsync);
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        // One ownership-scoped lookup gives missing and foreign IDs the same 404 response.
        var template = await db.TaskTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.FamilyId == user.FamilyId(), ct);
        if (template is null) return Results.NotFound();
        var version = await db.TaskTemplateVersions.AsNoTracking()
            .SingleAsync(v => v.TemplateId == id && v.Version == template.CurrentVersion, ct);
        return Results.Ok(TemplateDetail.From(template, version));
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
        return Results.Created($"/api/templates/{template.Id}", TemplateDetail.From(template, version));
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
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // The (TemplateId, Version) unique constraint also protects simultaneous publication.
            return VersionConflict();
        }
        return Results.Created($"/api/templates/{id}", TemplateDetail.From(template, version));
    }

    private static IResult VersionConflict() => Results.Problem(statusCode: 409,
        title: "This template has changed. Reload it before publishing another version.");
}
