using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>
/// Library or archive list entry without content or answer keys. <c>Difficulty</c> is read from the frozen plan in SQL,
/// <c>CreatedAtUtc</c> is the review time, <c>AssignedAtUtc</c> the latest open assignment, <c>CompletedAtUtc</c> the latest
/// completion, and <c>Children</c> lists open and completed work by child name.
/// </summary>
public sealed record SnapshotSummary(Guid Id, string Title, string Difficulty, DateTime CreatedAtUtc, DateTime? AssignedAtUtc,
    DateTime? CompletedAtUtc, DateTime? ArchivedAtUtc, SnapshotChild[] Children);
/// <summary>One child's work on a snapshot; completed work carries its final score from the frozen evaluation.</summary>
public sealed record SnapshotChild(string ChildName, string Status, int? FinalTotal, int? PossibleTotal);

/// <summary>
/// Frozen parent-only preview, including keys and release evidence. <c>FirstMessage</c> is the parent's first chat message
/// in the draft the activity began from, for starting another activity with it. Never use this DTO for child access.
/// </summary>
public sealed record SnapshotPreview(Guid Id, Guid SourceDraftId, long SourceDraftRevision, LearningPlan Plan, TaskDocument Document, LengthMeasurement[] Measurements, int EngineRevision,
    Guid? SourceSnapshotId, string CreatedByParentId, DateTime DraftCreatedAtUtc,
    string ReviewedByParentId, DateTime ReviewedAtUtc, DateTime? ArchivedAtUtc, string? FirstMessage);
