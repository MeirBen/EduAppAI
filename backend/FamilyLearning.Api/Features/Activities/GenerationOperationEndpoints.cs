using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Web;
using Serilog;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Parent-only durable start, status and cancel routes. Polling never starts work.</summary>
public static class GenerationOperationEndpoints
{
    public static void MapGenerationOperationEndpoints(this RouteGroupBuilder api)
    {
        var operations = api.MapGroup("/activity-drafts/{id:guid}/operations");
        operations.MapPost("/", StartAsync);
        operations.MapPost("/{operationId:guid}/cancel", CancelAsync);
        operations.MapGet("/{operationId:guid}", async (Guid id, Guid operationId, ClaimsPrincipal user, GenerationOperationService service, CancellationToken ct) =>
            (await service.FindAsync(user.FamilyId(), id, operationId, ct)).ToHttpResult());
    }

    private static async Task<IResult> StartAsync(Guid id, StartGenerationRequest body, ClaimsPrincipal user,
        GenerationOperationService service, IDiagnosticContext diagnostics, CancellationToken ct)
    {
        var result = await service.StartAsync(user.FamilyId(), id, body, ct);
        if (result.Value is { } operation) Record(operation, diagnostics);
        return result.ToHttpResult(static operation => Results.Accepted($"/api/activity-drafts/{operation.DraftId}/operations/{operation.Id}", operation));
    }

    private static async Task<IResult> CancelAsync(Guid id, Guid operationId, ClaimsPrincipal user,
        GenerationOperationService service, IDiagnosticContext diagnostics, CancellationToken ct)
    {
        var result = await service.CancelAsync(user.FamilyId(), id, operationId, ct);
        if (result.Value is { } operation) Record(operation, diagnostics);
        return result.ToHttpResult();
    }

    private static void Record(GenerationOperationDetail operation, IDiagnosticContext diagnostics)
    {
        diagnostics.Set("OperationId", operation.Id);
        diagnostics.Set("DraftId", operation.DraftId);
        diagnostics.Set("Status", operation.Status);
    }
}
