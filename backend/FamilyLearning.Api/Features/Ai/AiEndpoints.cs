using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Ai;

/// <summary>Parent-only plan chat: each submitted message permits one proposal call, never a publication.</summary>
public static class AiEndpoints
{
    public static void MapPlanAuthoringEndpoints(this RouteGroupBuilder api)
    {
        var ai = api.MapGroup("/ai");
        // New manual plans preserve the server's schema version; the client owns no version counter.
        ai.MapGet("/status", (AiGenerationService service) => Results.Ok(new { configured = service.Configured, schemaVersion = EngineVersions.SchemaVersion }));
        ai.MapPost("/template-drafts", async (TemplateAuthoringInput request, AiGenerationService service,
            AiStartLimiter limiter, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!limiter.TryAcquire(user.FamilyId())) return Results.StatusCode(429);
            var result = await service.AuthorAsync(request, ct);
            return Results.Ok(new
            {
                result.Value.Proposal,
                result.Value.Clarification,
                result.Value.Assumptions,
                result.Value.Changes,
                request.RequestId,
                request.BaseRevision,
                generationMetadata = result.Metadata
            });
        });
    }
}
