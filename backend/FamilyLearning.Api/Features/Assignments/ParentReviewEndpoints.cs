using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Maps parent-only result and grading requests to HTTP responses.</summary>
public static class ParentReviewEndpoints
{
    public static void MapParentReviewEndpoints(this RouteGroupBuilder assignments)
    {
        assignments.MapGet("/{id:guid}/result", async (Guid id, ClaimsPrincipal user, ParentReviewService service, CancellationToken ct) =>
            (await service.ReadAsync(id, user.FamilyId(), ct)).ToHttpResult());
        assignments.MapPost("/{id:guid}/review", async (Guid id, FinalizeReviewRequest request, ClaimsPrincipal user,
            ParentReviewService service, CancellationToken ct) =>
            (await service.FinalizeAsync(id, request, user.FamilyId(), user.FindFirstValue(ClaimTypes.NameIdentifier)!, ct)).ToHttpResult());
    }
}
