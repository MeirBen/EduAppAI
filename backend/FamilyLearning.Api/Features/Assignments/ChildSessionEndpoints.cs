using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Child-owned resumable work. Writes recheck current access after acquiring SQLite's transaction lock.</summary>
public static class ChildSessionEndpoints
{
    public static void MapChildSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/assignments/{id:guid}/session");
        sessions.MapPost("/", StartAsync);
        sessions.MapGet("/", ReadAsync);
        sessions.MapPut("/", (Guid id, SaveAnswersRequest request, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct) =>
            WriteAsync(id, request.ExpectedRevision, request.Answers, false, user, db, clock, ct));
        sessions.MapPost("/submit", (Guid id, SubmitAnswersRequest request, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct) =>
            WriteAsync(id, request.ExpectedRevision, request.Answers, true, user, db, clock, ct));
    }

    private static async Task<IResult> StartAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var utcNow = clock.GetUtcNow().UtcDateTime;
        var identity = await ChildAccess.FindAsync(user, db, utcNow, ct);
        if (identity is null) return Results.Unauthorized();
        var assignment = await Owned(db, id, identity).Include(a => a.Session).SingleOrDefaultAsync(ct);
        if (assignment is null) return Results.NotFound();
        if (assignment.Status == "withdrawn") return Withdrawn();
        if (assignment.Session is not null) return Results.Ok(LearnerSession.From(assignment.Session, assignment.Status));
        if (assignment.Status != "assigned") return Conflict();
        var session = new TaskSession(id, utcNow);
        db.TaskSessions.Add(session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/child/assignments/{id}/session", LearnerSession.From(session, assignment.Status));
    }

    private static async Task<IResult> ReadAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var identity = await ChildAccess.FindAsync(user, db, clock.GetUtcNow().UtcDateTime, ct);
        if (identity is null) return Results.Unauthorized();
        var assignment = await Owned(db, id, identity).AsNoTracking().Select(a => new { a.Status, a.Session }).SingleOrDefaultAsync(ct);
        if (assignment is null) return Results.NotFound();
        if (assignment.Status == "withdrawn") return Withdrawn();
        return assignment.Session is null ? Results.NotFound() : Results.Ok(LearnerSession.From(assignment.Session, assignment.Status));
    }

    private static async Task<IResult> WriteAsync(Guid id, long expectedRevision, SessionAnswer[]? answers, bool submitting,
        ClaimsPrincipal user, LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var utcNow = clock.GetUtcNow().UtcDateTime;
        var identity = await ChildAccess.FindAsync(user, db, utcNow, ct);
        if (identity is null) return Results.Unauthorized();
        var row = await Owned(db, id, identity)
            .Select(a => new { Assignment = a, a.Session, a.Snapshot.DocumentJson }).SingleOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();
        if (row.Assignment.Status == "withdrawn") return Withdrawn();
        var document = StoredJson.Read<TaskDocument>(row.DocumentJson);
        var errors = SessionValidation.Validate(document, answers, submitting);
        if (expectedRevision <= 0) errors["expectedRevision"] = ["יש לציין גרסה חיובית."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (row.Session is null) return Results.NotFound();
        if (row.Assignment.Status != "assigned")
        {
            // Bounds and ownership precede replay; comparison preserves every nonblank string exactly.
            if (submitting && SessionValidation.SameAnswers(StoredJson.Read<SessionAnswer[]>(row.Session.AnswersJson), answers!))
                return Results.Ok(LearnerSession.From(row.Session, row.Assignment.Status));
            return Conflict();
        }
        if (row.Session.Revision != expectedRevision) return Conflict();
        if (submitting)
        {
            var evaluation = SessionScoring.Evaluate(document, answers!);
            row.Session.Submit(answers!, evaluation, utcNow);
            row.Assignment.Submit(evaluation.PendingCount > 0);
        }
        else row.Session.Save(answers!, utcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(LearnerSession.From(row.Session, row.Assignment.Status));
    }

    private static IQueryable<Assignment> Owned(LearningDbContext db, Guid id, ChildIdentity identity) =>
        db.Assignments.Where(a => a.Id == id && a.ChildId == identity.ChildId && a.FamilyId == identity.FamilyId);

    private static IResult Withdrawn() => Results.Problem(statusCode: 410, title: "ההורה ביטל את ההקצאה הזו.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "התשובות השתנו או שכבר נשלחו. יש לבדוק את העבודה השמורה.");
}
