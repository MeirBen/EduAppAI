using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Read-only child inbox and allowlisted frozen content. Reads never start work or call AI.</summary>
public static class ChildAssignmentEndpoints
{
    public static void MapChildAssignmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/assignments", ListAsync);
        api.MapGet("/assignments/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var identity = await ChildAccess.FindAsync(user, db, clock.GetUtcNow().UtcDateTime, ct);
            if (identity is null) return Results.Unauthorized();
            var assignment = await db.Assignments.AsNoTracking().Where(a => a.Id == id && a.ChildId == identity.ChildId && a.FamilyId == identity.FamilyId)
                .Select(a => new { a.Id, a.Status, a.Revision, a.CreatedAtUtc, a.Snapshot.DocumentJson }).SingleOrDefaultAsync(ct);
            if (assignment is null) return Results.NotFound();
            if (assignment.Status == "withdrawn") return Results.Problem(statusCode: 410, title: "ההורה ביטל את ההקצאה הזו.");
            var document = LearnerDocument.From(StoredJson.Read<TaskDocument>(assignment.DocumentJson));
            return Results.Ok(new LearnerAssignment(assignment.Id, assignment.Status, assignment.Revision, assignment.CreatedAtUtc, document));
        });
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct,
        string state = "available", int page = 1, int pageSize = 25)
    {
        var identity = await ChildAccess.FindAsync(user, db, clock.GetUtcNow().UtcDateTime, ct);
        if (identity is null) return Results.Unauthorized();
        var paging = new PageRequest(page, pageSize);
        if (!paging.IsValid) return PageRequest.Invalid();
        if (state is not ("available" or "submitted"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["state"] = ["מצב הרשימה אינו תקף."] });
        var query = db.Assignments.AsNoTracking().Where(a => a.ChildId == identity.ChildId && a.FamilyId == identity.FamilyId);
        query = state == "available" ? query.Where(a => a.Status == "assigned") : query.Where(a => a.Status == "awaiting-review" || a.Status == "completed");
        return Results.Ok(await paging.ReadAsync(query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new LearnerAssignmentSummary(a.Id, a.Snapshot.Title, a.Status, a.Revision, a.CreatedAtUtc, a.Session != null)), ct));
    }
}
