using System.Security.Claims;
using FamilyLearning.Api.Infrastructure.Auth;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Validation;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Family-owned frozen reports and one-time parent grading; mapped only beneath the parent assignment group.</summary>
public static class ParentReviewEndpoints
{
    public static void MapParentReviewEndpoints(this RouteGroupBuilder assignments)
    {
        assignments.MapGet("/{id:guid}/result", ReadAsync);
        assignments.MapPost("/{id:guid}/review", FinalizeAsync);
    }

    private static async Task<IResult> ReadAsync(Guid id, ClaimsPrincipal user, LearningDbContext db, CancellationToken ct)
    {
        var row = await db.Assignments.AsNoTracking().Where(a => a.Id == id && a.FamilyId == user.FamilyId())
            .Select(a => new { Assignment = a, a.Session, ChildName = a.Child.Name, a.Snapshot.DocumentJson }).SingleOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();
        if (row.Session?.SubmittedAtUtc is null) return NotSubmitted();
        return Results.Ok(ParentAssignmentResult.From(row.Assignment, row.ChildName, row.DocumentJson, row.Session));
    }

    private static async Task<IResult> FinalizeAsync(Guid id, FinalizeReviewRequest request, ClaimsPrincipal user,
        LearningDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.Assignments.Where(a => a.Id == id && a.FamilyId == user.FamilyId())
            .Select(a => new { Assignment = a, a.Session, ChildName = a.Child.Name, a.Snapshot.DocumentJson }).SingleOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();
        var errors = ValidateShape(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
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
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["grades"] = ["יש לתת ציון לכל שאלה הממתינה לבדיקת הורה, פעם אחת בלבד ובטווח הנקודות שלה."] });
        if (completed)
        {
            if (request.Grades.Any(grade => !Array.Exists(expectedRows, q => q.QuestionId == grade.QuestionId && q.AwardedPoints == grade.Points)))
                return Conflict();
        }
        else
        {
            if (row.Session.Revision != request.ExpectedRevision) return Conflict();
            var result = SessionScoring.CompleteReview(evaluation, request.Grades);
            row.Session.CompleteReview(result, user.FindFirstValue(ClaimTypes.NameIdentifier)!, clock.GetUtcNow().UtcDateTime);
            row.Assignment.CompleteReview();
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Results.Ok(ParentAssignmentResult.From(row.Assignment, row.ChildName, row.DocumentJson, row.Session));
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

    private static IResult NotSubmitted() => Results.Problem(statusCode: 409, title: "הפעילות עדיין לא נשלחה לבדיקה.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "התוצאה השתנתה או שכבר הושלמה. יש לטעון את הדוח השמור.");
}
