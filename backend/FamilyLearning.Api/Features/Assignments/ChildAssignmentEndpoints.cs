using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Maps child inbox and frozen-content reads to learner-only HTTP responses.</summary>
public static class ChildAssignmentEndpoints
{
    public static void MapChildAssignmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/assignments", ListAsync);
        api.MapGet("/assignments/{id:guid}", async (Guid id, ClaimsPrincipal user, ChildAssignmentService service, CancellationToken ct) =>
            (await service.ReadAsync(id, user, ct)).ToHttpResult());
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ChildAssignmentService service, CancellationToken ct,
        string state = "available", int page = 1, int pageSize = PageRequest.DefaultSize) =>
        (await service.ListAsync(user, ct, state, page, pageSize)).ToHttpResult();
}
