using System.Security.Claims;
using FamilyLearning.Api.Features.Ai;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Parent operations that create, read and delete the family's saved task snapshots.</summary>
public static class InstanceEndpoints
{
    /// <summary>Maps authenticated draft routes onto the API group configured with CSRF protection.</summary>
    public static void MapInstanceEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/templates/{id:guid}/instances", CreateAsync);
        var instances = api.MapGroup("/instances");
        instances.MapGet("/", async (ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
            await db.TaskInstances.AsNoTracking().Where(i => i.FamilyId == user.FamilyId())
                .OrderByDescending(i => i.CreatedAtUtc).Take(100)
                .Select(i => new InstanceSummary(i.Id, i.Title, i.Status, i.CreatedAtUtc)).ToListAsync(ct));
        instances.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var snapshot = await (from instance in db.TaskInstances
                                  join version in db.TaskTemplateVersions on instance.TemplateVersionId equals version.Id
                                  where instance.Id == id && instance.FamilyId == user.FamilyId()
                                  select new { Instance = instance, version.Version }).AsNoTracking().SingleOrDefaultAsync(ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(InstancePreview.From(snapshot.Instance, snapshot.Version));
        });
        instances.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.TaskInstances.Where(i => i.Id == id && i.FamilyId == user.FamilyId()).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static async Task<IResult> CreateAsync(Guid id, TaskInput request, ClaimsPrincipal user,
        LearningDbContext db, AiGenerationService ai, AiStartLimiter limiter, CancellationToken ct)
    {
        // Resolve ownership and pin the current immutable revision in one database read.
        var version = await (from template in db.TaskTemplates
                             join revision in db.TaskTemplateVersions on template.Id equals revision.TemplateId
                             where template.Id == id && template.FamilyId == user.FamilyId() && revision.Version == template.CurrentVersion
                             select revision).AsNoTracking().SingleOrDefaultAsync(ct);
        if (version is null) return Results.NotFound();
        var definition = StoredJson.Read<TaskTemplateDefinition>(version.DefinitionJson);
        var parameters = ParameterValidator.Validate(definition.InstanceParameters, request.Parameters);
        var errors = TaskSettingsValidator.Validate(request.Settings);
        foreach (var error in parameters.Errors) errors.Add(error.Key, error.Value);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var input = new TaskInput(request.Settings, parameters.Values);
        if (!limiter.TryAcquire(user.FamilyId())) return Results.StatusCode(429);
        var generated = await ai.GenerateAsync(definition, input, ct);
        var instance = new TaskInstance(user.FamilyId(), version.Id, generated.Value.Title,
            StoredJson.Write(input), StoredJson.Write(generated.Value), StoredJson.Write(generated.Metadata));
        db.TaskInstances.Add(instance);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            // A template deletion/reset during the AI call must not resurrect content or return a server error.
            return Results.NotFound();
        }
        return Results.Created($"/api/instances/{instance.Id}", InstancePreview.From(instance, version.Version));
    }
}
