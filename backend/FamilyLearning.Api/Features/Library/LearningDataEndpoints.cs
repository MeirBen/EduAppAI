using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;

namespace FamilyLearning.Api.Features.Library;

/// <summary>Maps explicit family activity and child data deletions to HTTP.</summary>
public static class LearningDataEndpoints
{
    public static void MapLearningDataEndpoints(this RouteGroupBuilder api)
    {
        var data = api.MapGroup("/learning-data");
        data.MapDelete("/activities", async (ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
        {
            await service.DeleteActivitiesAsync(user.FamilyId(), ct);
            return Results.NoContent();
        });
        data.MapDelete("/children", async (ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
        {
            await service.DeleteChildrenAsync(user.FamilyId(), ct);
            return Results.NoContent();
        });
    }
}
