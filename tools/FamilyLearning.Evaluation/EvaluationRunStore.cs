using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyLearning.Evaluation;

public sealed record EvaluationHistory(string Id, string? Label, DateTime? StartedAtUtc, string? ConfiguredModel,
    EvaluationSummary? Summary, string? Error);
public sealed record EvaluationReviewUpdate(string CaseId, int Repetition, ManualReview Review);

/// <summary>Tool-only artifact boundary. Browser IDs never become arbitrary filesystem paths.</summary>
public sealed partial class EvaluationRunStore(string root) : IDisposable
{
    private readonly string root = Path.GetFullPath(root);
    private readonly SemaphoreSlim reviews = new(1, 1);

    public static string NewId() => $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";

    public string DirectoryFor(string id)
    {
        if (!RunId().IsMatch(id)) throw new ArgumentException("Invalid run ID.");
        var directory = Path.GetFullPath(Path.Combine(root, id));
        RejectLink(root);
        RejectLink(directory);
        RejectLink(Path.Combine(directory, "run.json"));
        RejectLink(Path.Combine(directory, "summary.json"));
        RejectLink(Path.Combine(directory, "run.json.tmp"));
        RejectLink(Path.Combine(directory, "summary.json.tmp"));
        return directory;
    }

    public Task<EvaluationReport> ReadAsync(string id) => EvaluationFiles.ReadReportAsync(Path.Combine(DirectoryFor(id), "run.json"));

    public async Task<EvaluationHistory[]> ListAsync()
    {
        RejectLink(root);
        if (!Directory.Exists(root)) return [];
        var history = new List<EvaluationHistory>();
        foreach (var directory in Directory.EnumerateDirectories(root).OrderDescending(StringComparer.Ordinal))
        {
            var id = Path.GetFileName(directory);
            if (!RunId().IsMatch(id)) continue;
            try
            {
                var report = await ReadAsync(id);
                history.Add(new(id, report.Label, report.StartedAtUtc, report.Profile.GetValueOrDefault("Model"), EvaluationSummary.Create(report), null));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                history.Add(new(id, null, null, null, null, "This report is unavailable or invalid."));
            }
        }
        return history.ToArray();
    }

    /// <summary>Serializes review-only changes. Active-run exclusion belongs to the coordinator.</summary>
    public async Task SaveReviewAsync(string id, EvaluationReviewUpdate update)
    {
        if (update.Review is null) throw new ArgumentException("A review is required.");
        update.Review.Validate();
        await reviews.WaitAsync();
        try
        {
            var report = await ReadAsync(id);
            if (report.Status == "running") throw new InvalidOperationException("A running report cannot be edited.");
            var result = report.Results.SingleOrDefault(item => item.CaseId == update.CaseId && item.Repetition == update.Repetition);
            if (result?.Generation is not { ContractValid: true, FinishedAtUtc: not null })
                throw new ArgumentException("Only completed generated results can be reviewed.");
            // Keep the captured evidence intact; only the six scores and reviewer notes are writable.
            result.Review.Hebrew = update.Review.Hebrew;
            result.Review.Correctness = update.Review.Correctness;
            result.Review.AgeFit = update.Review.AgeFit;
            result.Review.Adherence = update.Review.Adherence;
            result.Review.AnswerClarity = update.Review.AnswerClarity;
            result.Review.Consistency = update.Review.Consistency;
            result.Review.Notes = update.Review.Notes;
            await EvaluationFiles.SaveAsync(report, DirectoryFor(id));
        }
        finally { reviews.Release(); }
    }

    private static void RejectLink(string path)
    {
        if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked evaluation artifacts are not supported.");
    }

    [GeneratedRegex("\\A[0-9]{8}T[0-9]{6}Z-[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex RunId();

    public void Dispose() => reviews.Dispose();
}
