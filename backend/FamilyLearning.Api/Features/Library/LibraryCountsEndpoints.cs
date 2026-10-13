using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;

namespace FamilyLearning.Api.Features.Library;

/// <summary>Exposes the exact sizes of the family's library sections.</summary>
public static class LibraryCountsEndpoints
{
    public static void MapLibraryCountsEndpoints(this RouteGroupBuilder api) => api.MapGet("/library/counts",
        async (ClaimsPrincipal user, LibraryService service, CancellationToken ct) =>
            Results.Ok(await service.CountsAsync(user.FamilyId(), ct)));
}
