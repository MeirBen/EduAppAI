using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Reads child-owned inbox entries and allowlisted frozen content without starting work.</summary>
internal sealed class ChildAssignmentService(LearningDbContext db, ChildAccess access, TimeProvider clock)
{
    internal async Task<FeatureResult<LearnerAssignment>> ReadAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var identity = await access.FindAsync(user, clock.GetUtcNow().UtcDateTime, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var assignment = await db.Assignments.AsNoTracking().Where(a => a.Id == id && a.ChildId == identity.ChildId && a.FamilyId == identity.FamilyId)
            .Select(a => new { a.Id, a.Status, a.Revision, a.CreatedAtUtc, a.Snapshot.DocumentJson }).SingleOrDefaultAsync(ct);
        if (assignment is null) return FeatureFailure.NotFound;
        if (assignment.Status == "withdrawn") return FeatureFailure.Gone("ההורה ביטל את ההקצאה הזו.");
        var document = LearnerDocument.From(StoredJson.Read<TaskDocument>(assignment.DocumentJson));
        return new LearnerAssignment(assignment.Id, assignment.Status, assignment.Revision, assignment.CreatedAtUtc, document);
    }

    internal async Task<FeatureResult<PageResponse<LearnerAssignmentSummary>>> ListAsync(ClaimsPrincipal user, CancellationToken ct,
        string state = "available", int page = 1, int pageSize = PageRequest.DefaultSize)
    {
        var identity = await access.FindAsync(user, clock.GetUtcNow().UtcDateTime, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var paging = new PageRequest(page, pageSize);
        if (!paging.IsValid) return PageRequest.Invalid();
        if (state is not ("available" or "submitted"))
            return FeatureFailure.Invalid("state", "מצב הרשימה אינו תקף.");
        var query = db.Assignments.AsNoTracking().Where(a => a.ChildId == identity.ChildId && a.FamilyId == identity.FamilyId);
        query = state == "available" ? query.Where(a => a.Status == "assigned") : query.Where(a => a.Status == "awaiting-review" || a.Status == "completed");
        return await paging.ReadAsync(query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new LearnerAssignmentSummary(a.Id, a.Snapshot.Title,
                LearningDbContext.JsonText(a.Snapshot.PlanJson, "$.settings.difficulty")!, a.Status, a.Revision, a.CreatedAtUtc, a.Session != null,
                a.Status == "completed" ? LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.finalTotal") : null,
                a.Status == "completed" ? LearningDbContext.JsonInteger(a.Session!.EvaluationJson, "$.possibleTotal") : null)), ct);
    }
}
