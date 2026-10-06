using FamilyLearning.Api.Infrastructure.Persistence;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>One resumable answer buffer per assignment. Submission freezes answers and evaluation together.</summary>
/// <remarks>Assignment owns lifecycle status. Call mutations only after access/state/revision checks in a write transaction.</remarks>
public sealed class TaskSession(Guid assignmentId, DateTime startedAtUtc)
{
    public Guid AssignmentId { get; private set; } = assignmentId;
    public long Revision { get; private set; } = 1;
    public string AnswersJson { get; private set; } = "[]";
    public string? EvaluationJson { get; private set; }
    public int? ScoringPolicyVersion { get; private set; }
    public DateTime StartedAtUtc { get; private set; } = startedAtUtc;
    public DateTime? SavedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? ReviewedByParentId { get; private set; }
    public Assignment Assignment { get; private set; } = null!;

    /// <summary>Replaces a validated, unsubmitted buffer without changing assignment status or revision.</summary>
    public void Save(SessionAnswer[] answers, DateTime utcNow)
    {
        AnswersJson = StoredJson.Write(answers);
        SavedAtUtc = utcNow;
        Revision++;
    }

    /// <summary>Freezes validated final answers and their freshly computed evaluation; never called for replay.</summary>
    public void Submit(SessionAnswer[] answers, SessionEvaluation evaluation, DateTime utcNow)
    {
        AnswersJson = StoredJson.Write(answers);
        EvaluationJson = StoredJson.Write(evaluation);
        ScoringPolicyVersion = SessionScoring.PolicyVersion;
        SubmittedAtUtc = utcNow;
        Revision++;
    }

    /// <summary>Stores a completed review once, leaving frozen answers, submission time and scoring policy unchanged.</summary>
    public void CompleteReview(SessionEvaluation evaluation, string parentId, DateTime utcNow)
    {
        EvaluationJson = StoredJson.Write(evaluation);
        ReviewedByParentId = parentId;
        ReviewedAtUtc = utcNow;
        Revision++;
    }
}
