using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyLearning.Evaluation;

/// <summary>Versioned local artifacts. Summaries are derived; run.json is the only comparison input.</summary>
public static class EvaluationFiles
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // Local JSON, never interpolated into HTML; keep Hebrew readable for reviewers.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Reads and hashes the same bytes, so fixture changes during a run cannot alter its controls.</summary>
    public static async Task<(T[] Items, string Sha256)> LoadFixtureAsync<T>(string name)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, name));
        var items = JsonSerializer.Deserialize<T[]>(bytes, Json);
        if (items is not { Length: > 0 } || items.Any(item => item is null)) throw new InvalidDataException("Empty or null evaluation fixture.");
        return (items, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>Writes the authoritative checkpoint first; comparison always recomputes its summary.</summary>
    public static async Task SaveAsync(EvaluationReport report, string directory)
    {
        await WriteAsync("run.json", report);
        await WriteAsync("summary.json", EvaluationSummary.Create(report));

        async Task WriteAsync<T>(string name, T value)
        {
            var path = Path.Combine(directory, name);
            // Deliberately uncancellable: retain partial results after Ctrl+C. A failed summary cannot corrupt the run.
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(value, Json));
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }

    /// <summary>Rejects unsupported reports and invalid human scores instead of inventing missing measurements.</summary>
    public static async Task<EvaluationReport> ReadReportAsync(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Evaluation report is too large.");
        var report = JsonSerializer.Deserialize<EvaluationReport>(await File.ReadAllTextAsync(path), Json);
        if (report is null || report.FormatVersion != EvaluationReport.CurrentFormatVersion ||
            report.Cases is not { Length: > 0 } || report.Repeat is < 1 or > 5 || report.Profile is null ||
            string.IsNullOrWhiteSpace(report.SuiteSha256) || string.IsNullOrWhiteSpace(report.CalibrationSha256) ||
            string.IsNullOrWhiteSpace(report.JudgePromptVersion) || string.IsNullOrWhiteSpace(report.JudgePrompt) ||
            report.Results is null || report.Calibration is null || report.CalibrationSamples is not { Length: > 0 })
            throw new InvalidDataException("Invalid or unsupported evaluation report; format version 2 is required.");
        if (report.Cases.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Prompt)) ||
            report.Cases.Select(item => item.Id).Distinct().Count() != report.Cases.Length ||
            report.Results.Any(result => result is null || !report.Cases.Any(item => item.Id == result.CaseId) ||
                result.Repetition < 1 || result.Repetition > report.Repeat || result.Checks is null || result.Review is null ||
                (result.Judge?.ContractValid == true && result.Issues is null) ||
                result.Review.Scores().Values.Any(score => score is < 0 or > 2)) ||
            report.Results.Select(result => (result.CaseId, result.Repetition)).Distinct().Count() != report.Results.Count)
            throw new InvalidDataException("Invalid evaluation results or human scores; use 0, 1, 2 or null.");
        ValidateCalibrationSamples(report.CalibrationSamples);
        if (report.Calibration.Any(result => result is null || result.Sample is null || result.Call is null ||
                !report.CalibrationSamples.Any(sample => sample.Id == result.Sample.Id)) ||
            report.Calibration.Select(result => result.Sample.Id).Distinct().Count() != report.Calibration.Count)
            throw new InvalidDataException("Invalid calibration results.");
        ValidateCalibrationSamples(report.Calibration.Select(result => result.Sample).ToArray());
        if (report.Results.SelectMany(result => result.Issues ?? []).Concat(report.Calibration.SelectMany(result => result.Issues ?? []))
            .Any(issue => issue is null || !HebrewJudge.IsKnownKind(issue.Kind) || string.IsNullOrWhiteSpace(issue.Path) || string.IsNullOrWhiteSpace(issue.Quote)) ||
            report.Steps.Any(step => !double.IsFinite(step.ElapsedMilliseconds) || step.ElapsedMilliseconds < 0 ||
                step.InputTokens < 0 || step.OutputTokens < 0 || step.ReasoningTokens < 0 || step.CostCredits < 0))
            throw new InvalidDataException("Invalid findings or measurements.");
        return report;
    }

    /// <summary>Rejects unusable expectations before a live run can spend calls on them.</summary>
    public static void ValidateCalibrationSamples(CalibrationSample[] samples)
    {
        if (samples.Any(sample => sample is null || string.IsNullOrWhiteSpace(sample.Id) || string.IsNullOrWhiteSpace(sample.Request) ||
            sample.Texts is not { Length: > 0 } || sample.ExpectedIssues is null || sample.ExpectedIssues.Length > 20 ||
            sample.Texts.Any(text => text is null || string.IsNullOrWhiteSpace(text.Path) || string.IsNullOrWhiteSpace(text.Text)) ||
            sample.Texts.Select(text => text.Path).Distinct().Count() != sample.Texts.Length ||
            sample.ExpectedIssues.Any(expected => expected is null || string.IsNullOrWhiteSpace(expected.Quote) ||
                !sample.Texts.Any(text => text.Path == expected.Path && text.Text.Contains(expected.Quote, StringComparison.Ordinal))) ||
            sample.ExpectedIssues.Distinct().Count() != sample.ExpectedIssues.Length) ||
            samples.Select(sample => sample.Id).Distinct().Count() != samples.Length)
            throw new InvalidDataException("Calibration requires unique fields and expectations quoted from those fields.");
    }
}
