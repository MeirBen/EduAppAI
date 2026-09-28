using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Ai;

namespace FamilyLearning.Api.Features.Ai;

/// <summary>Parent-only transient authoring. No template is stored until the existing explicit save endpoint is called.</summary>
public static class AiEndpoints
{
    public static void MapAiEndpoints(this RouteGroupBuilder api)
    {
        var ai = api.MapGroup("/ai").RequireAuthorization("Parent");
        ai.MapGet("/status", (AiGenerationService service) => Results.Ok(new { configured = service.Configured }));
        ai.MapPost("/template-drafts", async (AuthorTemplateRequest request, AiGenerationService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 4000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["prompt"] = ["כתבו רעיון לתבנית, עד 4,000 תווים."] });
            var draft = await service.AuthorAsync(request.Prompt, ct);
            return Results.Ok(new { definition = draft.Value, generationMetadata = draft.Metadata });
        }).RequireRateLimiting("generation");
    }
}

/// <summary>A learning request only; identity and family IDs are never provider inputs.</summary>
public sealed record AuthorTemplateRequest([property: JsonRequired] string Prompt);
