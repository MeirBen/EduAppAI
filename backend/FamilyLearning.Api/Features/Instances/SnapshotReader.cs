using FamilyLearning.Api.Features.Activities;
using FamilyLearning.Api.Infrastructure.Persistence;
using FamilyLearning.Api.TaskEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Features.Instances;

/// <summary>Reads parent-only snapshot previews and the original requests behind copied activities.</summary>
internal static class SnapshotReader
{
    /// <summary>The caller must load a family-owned snapshot before reading its content, including answer keys.</summary>
    internal static async Task<SnapshotPreview> ReadAsync(LearningDbContext db, TaskSnapshot snapshot, CancellationToken ct) => new(
        snapshot.Id, snapshot.SourceDraftId, snapshot.SourceDraftRevision,
        StoredJson.Read<LearningPlan>(snapshot.PlanJson), StoredJson.Read<TaskDocument>(snapshot.DocumentJson),
        StoredJson.Read<LengthMeasurement[]>(snapshot.MeasurementsJson), snapshot.EngineRevision,
        snapshot.SourceSnapshotId, snapshot.CreatedByParentId, snapshot.DraftCreatedAtUtc,
        snapshot.ReviewedByParentId, snapshot.ReviewedAtUtc, snapshot.ArchivedAtUtc,
        await new FirstMessageReader(db, snapshot.FamilyId).ReadAsync(snapshot.Id, snapshot.SourceDraftId, ct));

    /// <summary>Returns at most five distinct first requests in latest approval order, excluding archived snapshots.</summary>
    internal static async Task<List<string>> FirstMessagesAsync(LearningDbContext db, Guid familyId, CancellationToken ct)
    {
        const int limit = 5;
        const int batchSize = 32;
        var snapshots = db.TaskSnapshots.AsNoTracking().Where(s => s.FamilyId == familyId && s.ArchivedAtUtc == null);
        var messages = new List<string>(limit);
        var reader = new FirstMessageReader(db, familyId);
        DateTime? lastReviewedAt = null;
        var lastId = Guid.Empty;
        while (messages.Count < limit)
        {
            var page = snapshots;
            // Immutable ordering keys keep removals from shifting an unread source behind an offset.
            if (lastReviewedAt is { } reviewedAt)
                page = page.Where(s => s.ReviewedAtUtc < reviewedAt || s.ReviewedAtUtc == reviewedAt && s.Id.CompareTo(lastId) < 0);
            var sources = await page.OrderByDescending(s => s.ReviewedAtUtc).ThenByDescending(s => s.Id).Take(batchSize)
                .Select(s => new { s.Id, s.SourceDraftId, s.ReviewedAtUtc }).ToListAsync(ct);
            foreach (var source in sources)
            {
                if (await reader.ReadAsync(source.Id, source.SourceDraftId, ct) is { } message && !messages.Contains(message))
                    messages.Add(message);
                if (messages.Count == limit) break;
            }
            if (sources.Count < batchSize) break;
            lastReviewedAt = sources[^1].ReviewedAtUtc;
            lastId = sources[^1].Id;
        }
        return messages;
    }

    /// <summary>Shares resolved ancestry only within one family read; deleted drafts end a chain with no message.</summary>
    /// <remarks>A root chat past its turn limit supplies its first retained parent turn.</remarks>
    private sealed class FirstMessageReader(LearningDbContext db, Guid familyId)
    {
        private readonly Dictionary<Guid, string?> messages = new();

        internal async Task<string?> ReadAsync(Guid snapshotId, Guid sourceDraftId, CancellationToken ct)
        {
            if (messages.TryGetValue(snapshotId, out var message)) return message;
            var ancestors = new List<Guid> { snapshotId };
            var drafts = db.ActivityDrafts.AsNoTracking().Where(d => d.FamilyId == familyId)
                .Select(d => new { d.Id, d.ReleasedSnapshotId, d.SourceSnapshotId, ChatJson = d.SourceSnapshotId == null ? d.ChatJson : null });
            var draft = await drafts.SingleOrDefaultAsync(d => d.Id == sourceDraftId, ct);
            while (draft?.SourceSnapshotId is { } source)
            {
                if (messages.TryGetValue(source, out message)) return Remember(message);
                ancestors.Add(source);
                // Draft provenance survives snapshot deletion; only root chat can supply the original request.
                draft = await drafts.SingleOrDefaultAsync(d => d.ReleasedSnapshotId == source, ct);
            }
            return Remember(draft is null ? null : StoredJson.Read<ActivityChatTurn[]>(draft.ChatJson!).FirstOrDefault(t => t.Role == "parent")?.Text);

            string? Remember(string? firstMessage)
            {
                foreach (var ancestor in ancestors) messages[ancestor] = firstMessage;
                return firstMessage;
            }
        }
    }
}
