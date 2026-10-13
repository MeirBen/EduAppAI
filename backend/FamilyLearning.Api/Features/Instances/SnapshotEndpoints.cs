using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Maps parent snapshot reads, library removal and archive deletion to HTTP.</summary>
public static class SnapshotEndpoints
{
    public static void MapSnapshotEndpoints(this RouteGroupBuilder api)
    {
        var snapshots = api.MapGroup("/instances");
        snapshots.MapGet("/", async (ClaimsPrincipal user, SnapshotService service, CancellationToken ct, string? status = null, int page = 1, int pageSize = PageRequest.DefaultSize) =>
            (await service.ListAsync(user.FamilyId(), archived: false, status, new PageRequest(page, pageSize), ct)).ToHttpResult());
        snapshots.MapGet("/first-messages", async (ClaimsPrincipal user, SnapshotService service, CancellationToken ct) =>
            Results.Ok(await service.FirstMessagesAsync(user.FamilyId(), ct)));
        snapshots.MapGet("/archive", async (ClaimsPrincipal user, SnapshotService service, CancellationToken ct, int page = 1, int pageSize = PageRequest.DefaultSize) =>
            (await service.ListAsync(user.FamilyId(), archived: true, null, new PageRequest(page, pageSize), ct)).ToHttpResult());
        snapshots.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, SnapshotService service, CancellationToken ct) =>
            (await service.ReadAsync(user.FamilyId(), id, ct)).ToHttpResult());
        snapshots.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, SnapshotService service, CancellationToken ct) =>
            (await service.RemoveAsync(user.FamilyId(), id, ct)).ToNoContentResult());
        snapshots.MapDelete("/archive/{id:guid}", async (Guid id, ClaimsPrincipal user, SnapshotService service, CancellationToken ct) =>
            (await service.DeleteArchivedAsync(user.FamilyId(), id, ct)).ToNoContentResult());
    }
}
