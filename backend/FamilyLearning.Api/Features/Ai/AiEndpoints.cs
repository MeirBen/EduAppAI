using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Ai;

/// <summary>Parent-only plan chat: each submitted message permits one proposal call without saving a draft.</summary>
public static class AiEndpoints
{
    public static void MapPlanAuthoringEndpoints(this RouteGroupBuilder api)
    {
        var ai = api.MapGroup("/ai");
        ai.MapGet("/status", (AiGenerationService service) => Results.Ok(new { configured = service.Configured }));
        ai.MapPost("/activity-plans", async (ActivityAuthoringInput request, AiGenerationService service, AiCapacity capacity,
            AiStartLimiter limiter, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!limiter.TryAcquire(user.FamilyId())) return Results.StatusCode(429);
            using var slot = capacity.TryEnter() ?? throw AiGenerationException.Busy();
            var result = await service.AuthorAsync(request, ct);
            return Results.Ok(new
            {
                result.Value.Proposal,
                result.Value.Reply,
                result.Value.Assumptions,
                result.Value.Changes,
                request.RequestId,
                request.BaseRevision,
                generationMetadata = result.Metadata
            });
        });
    }
}
