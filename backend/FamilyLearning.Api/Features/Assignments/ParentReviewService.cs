using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Reads family-owned frozen reports and serializes one-time parent grading and replay.</summary>
internal sealed class ParentReviewService(LearningDbContext db, TimeProvider clock)
{
    private sealed record ReviewRow(Assignment Assignment, TaskSession? Session, string ChildName, string DocumentJson, string Difficulty);

    private static IQueryable<ReviewRow> Rows(IQueryable<Assignment> assignments, Guid id, Guid familyId) =>
        assignments.Where(a => a.Id == id && a.FamilyId == familyId).Select(a => new ReviewRow(a, a.Session, a.Child.Name,
            a.Snapshot.DocumentJson, LearningDbContext.JsonText(a.Snapshot.PlanJson, "$.settings.difficulty")!));

    internal async Task<FeatureResult<ParentAssignmentResult>> ReadAsync(Guid id, Guid familyId, CancellationToken ct)
    {
        var row = await Rows(db.Assignments.AsNoTracking(), id, familyId).SingleOrDefaultAsync(ct);
        if (row is null) return FeatureFailure.NotFound;
        if (row.Session?.SubmittedAtUtc is null) return NotSubmitted();
        return ToParentResult(row, row.Session);
    }

    internal async Task<FeatureResult<ParentAssignmentResult>> FinalizeAsync(Guid id, FinalizeReviewRequest request,
        Guid familyId, string parentId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await Rows(db.Assignments, id, familyId).SingleOrDefaultAsync(ct);
        if (row is null) return FeatureFailure.NotFound;
        var errors = ValidateShape(request);
        if (errors.Count > 0) return FeatureFailure.Invalid(errors);
        if (row.Session?.SubmittedAtUtc is null) return NotSubmitted();
        var completed = row.Assignment.Status == "completed";
        if (row.Assignment.Status != "awaiting-review" && (!completed || row.Session.ReviewedByParentId is null))
            return Conflict();
        var evaluation = StoredJson.Read<SessionEvaluation>(row.Session.EvaluationJson!);
        // Completed replay checks the original parent rows; its pending set is now empty.
        var expectedRows = evaluation.Questions.Where(q => q.GradingMethod == "parent" &&
            (completed || q.AwardedPoints is null)).ToArray();
        if (request.Grades.Length != expectedRows.Length || request.Grades.Any(grade =>
            !Array.Exists(expectedRows, q => q.QuestionId == grade.QuestionId && grade.Points <= q.PossiblePoints)))
            return FeatureFailure.Invalid("grades", "יש לתת ציון לכל שאלה הממתינה לבדיקת הורה, פעם אחת בלבד ובטווח הנקודות שלה.");
        if (completed)
        {
            if (request.Grades.Any(grade => !Array.Exists(expectedRows, q => q.QuestionId == grade.QuestionId && q.AwardedPoints == grade.Points)))
                return Conflict();
        }
        else
        {
            if (row.Session.Revision != request.ExpectedRevision) return Conflict();
            var result = SessionScoring.CompleteReview(evaluation, request.Grades);
            row.Session.CompleteReview(result, parentId, clock.GetUtcNow().UtcDateTime);
            row.Assignment.CompleteReview();
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return ToParentResult(row, row.Session);
    }

    private static ParentAssignmentResult ToParentResult(ReviewRow row, TaskSession session)
    {
        var assignment = row.Assignment;
        var document = StoredJson.Read<TaskDocument>(row.DocumentJson);
        var evaluation = StoredJson.Read<SessionEvaluation>(session.EvaluationJson!);
        var completed = assignment.Status == "completed";
        var summary = new AssignmentSummary(assignment.Id, assignment.ChildId, row.ChildName, assignment.SnapshotId,
            document.Title, row.Difficulty, assignment.Status, assignment.Revision, assignment.CreatedAtUtc, true,
            completed ? evaluation.FinalTotal : null, completed ? evaluation.PossibleTotal : null);
        return new(summary, document, session.Revision, StoredJson.Read<SessionAnswer[]>(session.AnswersJson),
            evaluation, session.ScoringPolicyVersion!.Value,
            session.StartedAtUtc, session.SavedAtUtc, session.SubmittedAtUtc!.Value, session.ReviewedAtUtc, session.ReviewedByParentId);
    }

    private static Dictionary<string, string[]> ValidateShape(FinalizeReviewRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ExpectedRevision <= 0) errors["expectedRevision"] = ["יש לציין גרסה חיובית."];
        if (request.Grades is null || request.Grades.Length > EngineValidation.MaxQuestionCount)
        {
            errors["grades"] = ["יש לשלוח רשימת ציונים שאינה חורגת ממספר השאלות בפעילות."];
            return errors;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < request.Grades.Length; index++)
        {
            var grade = request.Grades[index];
            if (grade is null || string.IsNullOrEmpty(grade.QuestionId) || grade.QuestionId.Length > 32 ||
                !ids.Add(grade.QuestionId) || grade.Points is < 0 or > EngineValidation.MaxPoints)
                errors[$"grades[{index}]"] = ["יש לציין מזהה שאלה ייחודי וציון שלם בטווח הנקודות שלה."];
        }
        return errors;
    }

    private static FeatureFailure NotSubmitted() => FeatureFailure.Conflict("הפעילות עדיין לא נשלחה לבדיקה.");
    private static FeatureFailure Conflict() => FeatureFailure.Conflict("התוצאה השתנתה או שכבר הושלמה. יש לטעון את הדוח השמור.");
}
