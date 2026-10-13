using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Maps parent assignment requests to family-owned operations and HTTP responses.</summary>
public static class AssignmentEndpoints
{
    public static void MapAssignmentEndpoints(this RouteGroupBuilder api)
    {
        var assignments = api.MapGroup("/assignments");
        assignments.MapPost("/", async (CreateAssignmentRequest request, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
            (await service.CreateAsync(request, user.FamilyId(), ct)).ToHttpResult(static result => result.Created
                ? Results.Created($"/api/assignments/{result.Value.Id}", result.Value)
                : Results.Ok(result.Value)));
        assignments.MapGet("/", ListAsync);
        assignments.MapGet("/counts", async (ClaimsPrincipal user, AssignmentService service, CancellationToken ct, Guid? childId = null) =>
            Results.Ok(await service.CountsAsync(user.FamilyId(), ct, childId)));
        assignments.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
            (await service.ReadAsync(id, user.FamilyId(), ct)).ToHttpResult());
        assignments.MapPost("/{id:guid}/withdraw", async (Guid id, AssignmentStateRequest request, ClaimsPrincipal user,
            AssignmentService service, CancellationToken ct) =>
            (await service.WithdrawAsync(id, request, user.FamilyId(), ct)).ToHttpResult());
        assignments.MapPost("/{id:guid}/restore", async (Guid id, AssignmentStateRequest request, ClaimsPrincipal user,
            AssignmentService service, CancellationToken ct) =>
            (await service.RestoreAsync(id, request, user.FamilyId(), ct)).ToHttpResult());
        assignments.MapParentReviewEndpoints();
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, AssignmentService service, CancellationToken ct,
        Guid? childId = null, Guid? snapshotId = null, string? status = null, int page = 1,
        int pageSize = PageRequest.DefaultSize) =>
        (await service.ListAsync(user.FamilyId(), ct, childId, snapshotId, status, page, pageSize)).ToHttpResult();
}
