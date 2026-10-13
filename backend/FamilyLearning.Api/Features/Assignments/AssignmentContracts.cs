using System.Text.Json.Serialization;
using FamilyLearning.Api.Features.Instances;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Both IDs must belong to the parent family. An existing pair replays without creating another attempt.</summary>
public sealed record CreateAssignmentRequest([property: JsonRequired] Guid ChildId, [property: JsonRequired] Guid SnapshotId);
/// <summary>Withdrawal and restore apply only to the revision the parent saw.</summary>
public sealed record AssignmentStateRequest([property: JsonRequired] long ExpectedRevision);
/// <summary>
/// Parent list projection; fetch detail for content, never load content JSON to page this list. Difficulty is read from
/// the frozen plan in SQL; totals are set only for completed work and are read from the frozen evaluation in SQL.
/// </summary>
public sealed record AssignmentSummary(Guid Id, Guid ChildId, string ChildName, Guid SnapshotId, string Title, string Difficulty,
    string Status, long Revision, DateTime CreatedAtUtc, bool HasStarted, int? FinalTotal, int? PossibleTotal);
/// <summary>Parent-only assignment detail. Snapshot contains answer keys and must never serve a child route.</summary>
public sealed record AssignmentDetail(AssignmentSummary Assignment, SnapshotPreview Snapshot,
    DateTime? StartedAtUtc, DateTime? SavedAtUtc, DateTime? SubmittedAtUtc);
/// <summary>A child's list entry; totals appear only once the work is completed, as in the child's own session.</summary>
public sealed record LearnerAssignmentSummary(Guid Id, string Title, string Difficulty, string Status, long Revision, DateTime CreatedAtUtc, bool HasStarted,
    int? FinalTotal, int? PossibleTotal);
public sealed record LearnerAssignment(Guid Id, string Status, long Revision, DateTime CreatedAtUtc, LearnerDocument Document);

/// <summary>Explicit child allowlist; no parent document, answer, guidance or provenance object crosses this boundary.</summary>
public sealed record LearnerDocument(string Title, string? Instructions, LearnerMaterial[] Materials, LearnerQuestion[] Questions)
{
    internal static LearnerDocument From(TaskDocument document) => new(document.Title, document.Instructions,
        document.Materials.Select(m => new LearnerMaterial(m.Id, m.Title, m.Body)).ToArray(),
        document.Questions.Select(q => new LearnerQuestion(q.Id, q.Prompt,
            new LearnerInteraction(q.Interaction.Type, q.Interaction.Options?.ToArray()), q.Points)).ToArray());
}
public sealed record LearnerMaterial(string Id, string? Title, string Body);
public sealed record LearnerQuestion(string Id, string Prompt, LearnerInteraction Interaction, int Points);
public sealed record LearnerInteraction(string Type, string[]? Options);

/// <summary>Sizes of the parent list's views: open (assigned or awaiting review), completed and withdrawn.</summary>
public sealed record AssignmentCounts(int Open, int Completed, int Withdrawn);
