using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Required raw answer text; nonblank values are preserved exactly, including during submission.</summary>
public sealed record SessionAnswer([property: JsonRequired] string QuestionId, [property: JsonRequired] string Value);
/// <summary>Replaces the entire saved buffer at the expected session revision.</summary>
public sealed record SaveAnswersRequest([property: JsonRequired] long ExpectedRevision, [property: JsonRequired] SessionAnswer[] Answers);
/// <summary>Freezes the final buffer. Identical submitted answers replay the stored outcome, even with an old revision.</summary>
public sealed record SubmitAnswersRequest([property: JsonRequired] long ExpectedRevision, [property: JsonRequired] SessionAnswer[] Answers);
/// <summary>Frozen award; a null award requires parent review. Never expose individual awards through child APIs.</summary>
public sealed record QuestionEvaluation(string QuestionId, int PossiblePoints, int? AwardedPoints, string GradingMethod);
/// <summary>Persisted submission result; final total is null while any parent grade is pending.</summary>
public sealed record SessionEvaluation(QuestionEvaluation[] Questions, int AutomaticSubtotal, int PossibleTotal, int PendingCount, int? FinalTotal);

/// <summary>Integer points for one parent-graded question, bounded by its frozen possible points.</summary>
public sealed record ParentGrade([property: JsonRequired] string QuestionId, [property: JsonRequired] int Points);
/// <summary>Finalizes every pending grade at the session revision; identical completed grades replay the original report.</summary>
public sealed record FinalizeReviewRequest([property: JsonRequired] long ExpectedRevision, [property: JsonRequired] ParentGrade[] Grades);

/// <summary>Parent-only submitted report. Content and awards come from stored snapshots, never from a live plan or rescoring.</summary>
/// <remarks>Final total stays null while review is pending. Percentage presentation must also require a positive possible total.</remarks>
public sealed record ParentAssignmentResult(AssignmentSummary Assignment, TaskDocument Document, long Revision,
    SessionAnswer[] Answers, SessionEvaluation Evaluation, int ScoringPolicyVersion, DateTime StartedAtUtc,
    DateTime? SavedAtUtc, DateTime SubmittedAtUtc, DateTime? ReviewedAtUtc, string? ReviewedByParentId);

/// <summary>Child-only receipt and saved answers. No keys, question awards, reviewer identity or parent report is exposed.</summary>
public sealed record LearnerSession(Guid AssignmentId, long Revision, string Status, SessionAnswer[] Answers,
    DateTime StartedAtUtc, DateTime? SavedAtUtc, DateTime? SubmittedAtUtc, DateTime? ReviewedAtUtc, int? FinalTotal, int? PossibleTotal);
