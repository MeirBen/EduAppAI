using System.Security.Claims;
using System.Text.Json.Serialization;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Ai;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Ai;

/// <summary>Returns parent-only AI template proposals without publishing them.</summary>
public static class AiEndpoints
{
    /// <summary>Maps the bounded plan chat contract in the staged API; each submitted message permits one proposal call.</summary>
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

    public static void MapAiEndpoints(this RouteGroupBuilder api)
    {
        var ai = api.MapGroup("/ai");
        ai.MapGet("/status", (AiGenerationService service) => Results.Ok(new { configured = service.Configured }));
        ai.MapPost("/template-drafts", async (AuthorTemplateRequest request, AiGenerationService service, AiStartLimiter limiter, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 4000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["prompt"] = ["כתבו רעיון לתבנית, עד 4,000 תווים."] });
            if (!limiter.TryAcquire(user.FamilyId())) return Results.StatusCode(429);
            var draft = await service.AuthorAsync(request.Prompt, ct);
            return Results.Ok(new { definition = draft.Value, generationMetadata = draft.Metadata });
        });
    }
}

/// <summary>A learning request only; identity and family IDs are never provider inputs.</summary>
public sealed record AuthorTemplateRequest([property: JsonRequired] string Prompt);
