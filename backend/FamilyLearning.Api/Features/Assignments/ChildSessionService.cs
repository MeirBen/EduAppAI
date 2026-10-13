using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Owns child sessions and rechecks current access after acquiring SQLite's write transaction lock.</summary>
internal sealed class ChildSessionService(LearningDbContext db, ChildAccess access, TimeProvider clock)
{
    internal async Task<FeatureResult<(LearnerSession Value, bool Created)>> StartAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var utcNow = clock.GetUtcNow().UtcDateTime;
        var identity = await access.FindAsync(user, utcNow, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var assignment = await Owned(id, identity).Include(a => a.Session).SingleOrDefaultAsync(ct);
        if (assignment is null) return FeatureFailure.NotFound;
        if (assignment.Status == "withdrawn") return Withdrawn();
        if (assignment.Session is not null) return (ToLearnerSession(assignment.Session, assignment.Status), false);
        if (assignment.Status != "assigned") return Conflict();
        var session = new TaskSession(id, utcNow);
        db.TaskSessions.Add(session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (ToLearnerSession(session, assignment.Status), true);
    }

    internal async Task<FeatureResult<LearnerSession>> ReadAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var identity = await access.FindAsync(user, clock.GetUtcNow().UtcDateTime, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var assignment = await Owned(id, identity).AsNoTracking().Select(a => new { a.Status, a.Session }).SingleOrDefaultAsync(ct);
        if (assignment is null) return FeatureFailure.NotFound;
        if (assignment.Status == "withdrawn") return Withdrawn();
        return assignment.Session is null ? FeatureFailure.NotFound : ToLearnerSession(assignment.Session, assignment.Status);
    }

    internal async Task<FeatureResult<LearnerSession>> WriteAsync(Guid id, long expectedRevision, SessionAnswer[]? answers, bool submitting,
        ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var utcNow = clock.GetUtcNow().UtcDateTime;
        var identity = await access.FindAsync(user, utcNow, ct);
        if (identity is null) return FeatureFailure.Unauthorized;
        var row = await Owned(id, identity)
            .Select(a => new { Assignment = a, a.Session, a.Snapshot.DocumentJson }).SingleOrDefaultAsync(ct);
        if (row is null) return FeatureFailure.NotFound;
        if (row.Assignment.Status == "withdrawn") return Withdrawn();
        var document = StoredJson.Read<TaskDocument>(row.DocumentJson);
        var errors = SessionValidation.Validate(document, answers, submitting);
        if (expectedRevision <= 0) errors["expectedRevision"] = ["יש לציין גרסה חיובית."];
        if (errors.Count > 0) return FeatureFailure.Invalid(errors);
        if (row.Session is null) return FeatureFailure.NotFound;
        if (row.Assignment.Status != "assigned")
        {
            // Bounds and ownership precede replay; comparison preserves every nonblank string exactly.
            if (submitting && SessionValidation.SameAnswers(StoredJson.Read<SessionAnswer[]>(row.Session.AnswersJson), answers!))
                return ToLearnerSession(row.Session, row.Assignment.Status);
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
        return ToLearnerSession(row.Session, row.Assignment.Status);
    }

    private static LearnerSession ToLearnerSession(TaskSession session, string status)
    {
        var evaluation = session.EvaluationJson is null ? null : StoredJson.Read<SessionEvaluation>(session.EvaluationJson);
        return new(session.AssignmentId, session.Revision, status, StoredJson.Read<SessionAnswer[]>(session.AnswersJson),
            session.StartedAtUtc, session.SavedAtUtc, session.SubmittedAtUtc, session.ReviewedAtUtc,
            status == "completed" ? evaluation?.FinalTotal : null, evaluation?.PossibleTotal);
    }

    private IQueryable<Assignment> Owned(Guid id, ChildIdentity identity) =>
        db.Assignments.Where(a => a.Id == id && a.ChildId == identity.ChildId && a.FamilyId == identity.FamilyId);

    private static FeatureFailure Withdrawn() => FeatureFailure.Gone("ההורה ביטל את ההקצאה הזו.");
    private static FeatureFailure Conflict() => FeatureFailure.Conflict("התשובות השתנו או שכבר נשלחו. יש לבדוק את העבודה השמורה.");
}
