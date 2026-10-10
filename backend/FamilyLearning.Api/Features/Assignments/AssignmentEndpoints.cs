using System.Security.Claims;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Family-owned assignment creation, inspection, withdrawal and restore with serialized state checks.</summary>
public static class AssignmentEndpoints
{
    public static void MapAssignmentEndpoints(this RouteGroupBuilder api)
    {
        var assignments = api.MapGroup("/assignments");
        assignments.MapPost("/", CreateAsync);
        assignments.MapGet("/", ListAsync);
        assignments.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct) =>
        {
            var query = db.Assignments.AsNoTracking().Where(a => a.Id == id && a.FamilyId == user.FamilyId());
            var summary = await query.Select(AssignmentSummary.Projection).SingleOrDefaultAsync(ct);
            if (summary is null) return Results.NotFound();
            var snapshot = await db.TaskSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == summary.SnapshotId && s.FamilyId == user.FamilyId(), ct);
            if (snapshot is null) return Results.NotFound();
            var timing = await db.TaskSessions.AsNoTracking().Where(s => s.AssignmentId == id)
                .Select(s => new { s.StartedAtUtc, s.SavedAtUtc, s.SubmittedAtUtc }).SingleOrDefaultAsync(ct);
            return Results.Ok(new AssignmentDetail(summary, SnapshotPreview.From(snapshot), timing?.StartedAtUtc, timing?.SavedAtUtc, timing?.SubmittedAtUtc));
        });
        assignments.MapPost("/{id:guid}/withdraw", WithdrawAsync);
        assignments.MapPost("/{id:guid}/restore", RestoreAsync);
        assignments.MapParentReviewEndpoints();
    }

    private static async Task<IResult> CreateAsync(CreateAssignmentRequest request, ClaimsPrincipal user, LearningDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var familyId = user.FamilyId();
        // Replay precedes eligibility: disabling a profile or archiving content cannot reopen or erase existing work.
        var existing = await db.Assignments.AsNoTracking().Where(a => a.FamilyId == familyId &&
            a.ChildId == request.ChildId && a.SnapshotId == request.SnapshotId).Select(AssignmentSummary.Projection).SingleOrDefaultAsync(ct);
        if (existing is not null) return Results.Ok(existing);
        var child = await db.Children.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.ChildId && c.FamilyId == familyId, ct);
        var snapshot = await db.TaskSnapshots.AsNoTracking().Where(s => s.Id == request.SnapshotId && s.FamilyId == familyId)
            .Select(s => new { s.ArchivedAtUtc }).SingleOrDefaultAsync(ct);
        if (child is null || snapshot is null) return Results.NotFound();
        if (!child.Enabled || snapshot.ArchivedAtUtc is not null) return Ineligible();
        var assignment = new Assignment(familyId, child.Id, request.SnapshotId, clock.GetUtcNow().UtcDateTime);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        var summary = await db.Assignments.Where(a => a.Id == assignment.Id).Select(AssignmentSummary.Projection).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/assignments/{assignment.Id}", summary);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, LearningDbContext db, CancellationToken ct,
        Guid? childId = null, string? status = null, int page = 1, int pageSize = 25)
    {
        var paging = new PageRequest(page, pageSize);
        if (!paging.IsValid) return PageRequest.Invalid();
        if (status is not (null or "assigned" or "withdrawn" or "awaiting-review" or "completed"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["מצב ההקצאה אינו תקף."] });
        var query = db.Assignments.AsNoTracking().Where(a => a.FamilyId == user.FamilyId());
        if (childId.HasValue) query = query.Where(a => a.ChildId == childId.Value);
        // Withdrawn work is history: only its own filter lists it.
        query = status is null ? query.Where(a => a.Status != "withdrawn") : query.Where(a => a.Status == status);
        return Results.Ok(await paging.ReadAsync(query.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Select(AssignmentSummary.Projection), ct));
    }

    private static async Task<IResult> WithdrawAsync(Guid id, AssignmentStateRequest request, ClaimsPrincipal user,
        LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id && a.FamilyId == user.FamilyId(), ct);
        if (assignment is null) return Results.NotFound();
        if (request.ExpectedRevision <= 0) return InvalidRevision();
        if (assignment.Status != "withdrawn")
        {
            if (assignment.Status != "assigned" || assignment.Revision != request.ExpectedRevision) return Changed();
            assignment.Withdraw(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }
        var summary = await db.Assignments.Where(a => a.Id == id).Select(AssignmentSummary.Projection).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(summary);
    }

    /// <summary>An explicit undo of withdrawal; creation never restores, so a delayed duplicate cannot undo a later withdrawal.</summary>
    private static async Task<IResult> RestoreAsync(Guid id, AssignmentStateRequest request, ClaimsPrincipal user,
        LearningDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var assignment = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id && a.FamilyId == user.FamilyId(), ct);
        if (assignment is null) return Results.NotFound();
        if (request.ExpectedRevision <= 0) return InvalidRevision();
        if (assignment.Status != "assigned")
        {
            if (assignment.Status != "withdrawn" || assignment.Revision != request.ExpectedRevision) return Changed();
            // Restoring reopens access, so it needs the same eligibility as a new assignment.
            if (!await db.Children.AnyAsync(c => c.Id == assignment.ChildId && c.Enabled, ct) ||
                !await db.TaskSnapshots.AnyAsync(s => s.Id == assignment.SnapshotId && s.ArchivedAtUtc == null, ct))
                return Ineligible();
            assignment.Restore();
            await db.SaveChangesAsync(ct);
        }
        var summary = await db.Assignments.Where(a => a.Id == id).Select(AssignmentSummary.Projection).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(summary);
    }

    private static IResult InvalidRevision() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["expectedRevision"] = ["יש לציין גרסה חיובית."] });
    private static IResult Changed() => Results.Problem(statusCode: 409, title: "ההקצאה השתנתה או שכבר נשלחה. יש לטעון אותה מחדש.");
    private static IResult Ineligible() => Results.Problem(statusCode: 409, title: "אפשר להקצות פעילות רק לילד פעיל ומתוך הספרייה הפעילה.");
}
