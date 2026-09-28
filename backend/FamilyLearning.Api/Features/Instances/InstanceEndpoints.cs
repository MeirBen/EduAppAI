using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Parent operations that create frozen drafts and read the family's saved content.</summary>
public static class InstanceEndpoints
{
    /// <summary>Maps authenticated draft routes onto the API group configured with CSRF protection.</summary>
    public static void MapInstanceEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/templates/{id:guid}/instances", CreateAsync).RequireAuthorization("Parent").RequireRateLimiting("generation");
        var instances = api.MapGroup("/instances").RequireAuthorization("Parent");
        instances.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskInstances.AsNoTracking().Where(i => i.FamilyId == user.FamilyId())
                .OrderByDescending(i => i.CreatedAtUtc).Take(100)
                .Select(i => new InstanceSummary(i.Id, i.Title, i.Status, i.CreatedAtUtc)).ToListAsync(ct));
        instances.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var instance = await db.TaskInstances.AsNoTracking()
                .SingleOrDefaultAsync(i => i.Id == id && i.FamilyId == user.FamilyId(), ct);
            if (instance is null) return Results.NotFound();
            var version = await db.TaskTemplateVersions.AsNoTracking().Where(v => v.Id == instance.TemplateVersionId)
                .Select(v => v.Version).SingleAsync(ct);
            return Results.Ok(InstancePreview.From(instance, version));
        });
    }

    private static async Task<IResult> CreateAsync(Guid id, CreateInstanceRequest request, ClaimsPrincipal user,
        LearningDbContext db, AiGenerationService ai, CancellationToken ct)
    {
        var template = await db.TaskTemplates.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id && t.FamilyId == user.FamilyId(), ct);
        if (template is null) return Results.NotFound();
        // Pin the revision we read; concurrent publication cannot change this immutable definition.
        var version = await db.TaskTemplateVersions.AsNoTracking()
            .SingleAsync(v => v.TemplateId == id && v.Version == template.CurrentVersion, ct);
        var definition = StoredJson.Read<TaskTemplateDefinition>(version.DefinitionJson);
        var parameters = ParameterValidator.Validate(definition.InstanceParameters, request.Parameters);
        if (parameters.Errors.Count > 0) return Results.ValidationProblem(parameters.Errors);
        var generated = await ai.GenerateAsync(definition, parameters.Values, ct);
        var instance = new TaskInstance(user.FamilyId(), version.Id, generated.Value.Title,
            StoredJson.Write(parameters.Values), StoredJson.Write(generated.Value), StoredJson.Write(generated.Metadata));
        db.TaskInstances.Add(instance);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/instances/{instance.Id}", InstancePreview.From(instance, version.Version));
    }
}
