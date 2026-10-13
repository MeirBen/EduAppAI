using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Web;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Maps child session requests to learner-only HTTP responses.</summary>
public static class ChildSessionEndpoints
{
    public static void MapChildSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/assignments/{id:guid}/session");
        sessions.MapPost("/", async (Guid id, ClaimsPrincipal user, ChildSessionService service, CancellationToken ct) =>
            (await service.StartAsync(id, user, ct)).ToHttpResult(static result => result.Created
                ? Results.Created($"/api/child/assignments/{result.Value.AssignmentId}/session", result.Value)
                : Results.Ok(result.Value)));
        sessions.MapGet("/", async (Guid id, ClaimsPrincipal user, ChildSessionService service, CancellationToken ct) =>
            (await service.ReadAsync(id, user, ct)).ToHttpResult());
        sessions.MapPut("/", async (Guid id, SaveAnswersRequest request, ClaimsPrincipal user, ChildSessionService service, CancellationToken ct) =>
            (await service.WriteAsync(id, request.ExpectedRevision, request.Answers, false, user, ct)).ToHttpResult());
        sessions.MapPost("/submit", async (Guid id, SubmitAnswersRequest request, ClaimsPrincipal user, ChildSessionService service, CancellationToken ct) =>
            (await service.WriteAsync(id, request.ExpectedRevision, request.Answers, true, user, ct)).ToHttpResult());
    }
}
