using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Parent-only draft checkpoints, adoption and immutable release. No endpoint in this feature calls a provider.</summary>
public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this RouteGroupBuilder api)
    {
        var drafts = api.MapGroup("/activity-drafts");
        drafts.MapPost("/", async (CreateActivityRequest body, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.CreateAsync(user.FamilyId(), user.FindFirstValue(ClaimTypes.NameIdentifier)!, body, ct))
                .ToHttpResult(static result => result.Created
                    ? Results.Created($"/api/activity-drafts/{result.Value.Id}", result.Value) : Results.Ok(result.Value)));
        drafts.MapGet("/", async (ClaimsPrincipal user, ActivityService service, CancellationToken ct,
            int page = 1, int pageSize = PageRequest.DefaultSize) =>
            (await service.ListAsync(user.FamilyId(), new(page, pageSize), ct)).ToHttpResult());
        drafts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.FindAsync(user.FamilyId(), id, ct)).ToHttpResult());
        drafts.MapPut("/{id:guid}", async (Guid id, SaveActivityRequest body, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.SaveAsync(user.FamilyId(), id, body, ct)).ToHttpResult());
        drafts.MapPost("/{id:guid}/adopt-content", async (Guid id, AdoptActivityRequest body, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.AdoptAsync(user.FamilyId(), id, body, ct)).ToHttpResult());
        drafts.MapPost("/{id:guid}/release", async (Guid id, ReleaseActivityRequest body, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.ReleaseAsync(user.FamilyId(), user.FindFirstValue(ClaimTypes.NameIdentifier)!, id, body.ExpectedRevision, ct))
                .ToHttpResult(static result => result.Created
                    ? Results.Created($"/api/instances/{result.Value.Id}", result.Value) : Results.Ok(result.Value)));
        drafts.MapPost("/{id:guid}/undo", async (Guid id, UndoActivityRequest body, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.UndoAsync(user.FamilyId(), id, body, ct)).ToHttpResult());
        drafts.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ActivityService service, CancellationToken ct) =>
            (await service.DeleteAsync(user.FamilyId(), id, ct)).ToNoContentResult());
    }
}
