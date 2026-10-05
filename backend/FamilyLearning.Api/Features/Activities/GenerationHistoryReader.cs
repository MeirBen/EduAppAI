using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Activities;

/// <summary>Captures the family's recent material ideas and question prompts once at admission, without keys or identities.</summary>
internal static class GenerationHistoryReader
{
    private const int RecentDocumentLimit = 12;
    private const int IdeaLimit = 8;
    private const int QuestionLimit = 12;
    private const int QuestionLength = 300;

    /// <summary>Reads <paramref name="current"/> first, then the most recent other drafts and snapshots, deduplicating copies.</summary>
    internal static async Task<GenerationHistory> ReadAsync(LearningDbContext db, Guid familyId, Guid draftId,
        TaskDocument current, CancellationToken ct)
    {
        // A released draft duplicates its snapshot, so only unreleased drafts are read.
        var drafts = await db.ActivityDrafts.AsNoTracking()
            .Where(d => d.FamilyId == familyId && d.Id != draftId && d.ReleasedSnapshotId == null)
            .OrderByDescending(d => d.UpdatedAtUtc).ThenBy(d => d.Id).Take(RecentDocumentLimit)
            .Select(d => new HistoryDocument(d.UpdatedAtUtc, d.DocumentJson)).ToListAsync(ct);
        var snapshots = await db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == familyId)
            .OrderByDescending(s => s.ReviewedAtUtc).ThenBy(s => s.Id).Take(RecentDocumentLimit)
            .Select(s => new HistoryDocument(s.ReviewedAtUtc, s.DocumentJson)).ToListAsync(ct);
        var ideas = new List<MaterialIdea>();
        var questions = new List<string>();
        Add(current);
        foreach (var row in drafts.Concat(snapshots).OrderByDescending(d => d.Time))
        {
            if (ideas.Count == IdeaLimit && questions.Count == QuestionLimit) break;
            Add(StoredJson.Read<TaskDocument>(row.Document));
        }
        return new(ideas.ToArray(), questions.ToArray());

        void Add(TaskDocument document)
        {
            foreach (var idea in document.Materials.Select(m => m.Idea).OfType<MaterialIdea>())
                if (ideas.Count < IdeaLimit && !ideas.Contains(idea)) ideas.Add(idea);
            foreach (var question in document.Questions.Where(q => !string.IsNullOrWhiteSpace(q.Prompt)))
            {
                var prompt = question.Prompt.Trim();
                if (prompt.Length > QuestionLength) prompt = prompt[..QuestionLength];
                if (questions.Count < QuestionLimit && !questions.Contains(prompt)) questions.Add(prompt);
            }
        }
    }

    private sealed record HistoryDocument(DateTime Time, string Document);
}
