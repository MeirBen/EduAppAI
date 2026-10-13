using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Children;

/// <summary>Maps parent-owned profile and device requests to feature outcomes.</summary>
public static class ChildEndpoints
{
    public static void MapChildEndpoints(this RouteGroupBuilder api)
    {
        var children = api.MapGroup("/children");
        children.MapGet("/", ListAsync);
        children.MapPost("/", async (CreateChildRequest request, ClaimsPrincipal user, ChildService service, CancellationToken ct) =>
            (await service.CreateAsync(user.FamilyId(), request, ct))
                .ToHttpResult(static child => Results.Created($"/api/children/{child.Id}", child)));
        children.MapPut("/{id:guid}", async (Guid id, UpdateChildRequest request, ClaimsPrincipal user, ChildService service, CancellationToken ct) =>
            (await service.UpdateAsync(user.FamilyId(), id, request, ct)).ToHttpResult());
        children.MapDelete("/{id:guid}", async (Guid id, long expectedRevision, ClaimsPrincipal user, ChildService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.FamilyId(), id, expectedRevision, ct)).ToNoContentResult());
        children.MapPost("/{id:guid}/activation", async (Guid id, CreateActivationRequest request, ClaimsPrincipal user, ChildService service, CancellationToken ct) =>
            (await service.IssueAsync(user.FamilyId(), id, request, ct)).ToHttpResult()).RequireRateLimiting("child-issuance");
        children.MapGet("/{id:guid}/devices", DevicesAsync);
        children.MapDelete("/{id:guid}/devices/{grantId:guid}", async (Guid id, Guid grantId, ClaimsPrincipal user, ChildService service, CancellationToken ct) =>
            (await service.RevokeAsync(user.FamilyId(), id, grantId, ct)).ToNoContentResult());
        children.MapDelete("/{id:guid}/devices/{grantId:guid}/record", async (Guid id, Guid grantId, ClaimsPrincipal user, ChildService service, CancellationToken ct) =>
            (await service.RemoveDeviceAsync(user.FamilyId(), id, grantId, ct)).ToNoContentResult());
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ChildService service, CancellationToken ct,
        int page = 1, int pageSize = PageRequest.DefaultSize) =>
        (await service.ListAsync(user.FamilyId(), new(page, pageSize), ct)).ToHttpResult();

    private static async Task<IResult> DevicesAsync(Guid id, ClaimsPrincipal user, ChildService service, CancellationToken ct,
        int page = 1, int pageSize = PageRequest.DefaultSize) =>
        (await service.DevicesAsync(user.FamilyId(), id, new(page, pageSize), ct)).ToHttpResult();
}
