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
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // Local JSON, never interpolated into HTML; keep Hebrew readable for reviewers.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Reads and hashes the same bytes, so fixture changes during a run cannot alter its controls.</summary>
    public static async Task<(T[] Items, string Sha256)> LoadFixtureAsync<T>(string name, string? directory = null)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory ?? AppContext.BaseDirectory, name));
        var items = JsonSerializer.Deserialize<T[]>(bytes, Json);
        if (items is not { Length: > 0 } || items.Any(item => item is null)) throw new InvalidDataException("Empty or null evaluation fixture.");
        if (items is EvaluationCase[] cases) ValidateCases(cases);
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
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path), new() { MaxDepth = Json.MaxDepth });
        var report = document.RootElement.Deserialize<EvaluationReport>(Json);
        if (report is null || report.FormatVersion != EvaluationReport.CurrentFormatVersion ||
            report.Cases is not { Length: > 0 } || report.Repeat is < 1 or > 5 || report.Profile is null ||
            string.IsNullOrWhiteSpace(report.SuiteSha256) || report.Results is null || report.Calibration is null || report.CalibrationSamples is null)
            throw new InvalidDataException("Invalid or unsupported evaluation report; format version 2 is required.");
        // Defaults permit non-judge format-2 reports to omit controls; they must not repair missing judge evidence.
        if (report.JudgeEnabled && (string.IsNullOrWhiteSpace(report.CalibrationSha256) || report.CalibrationSamples.Length == 0 ||
            string.IsNullOrWhiteSpace(report.JudgePromptVersion) || string.IsNullOrWhiteSpace(report.JudgePrompt) ||
            !document.RootElement.EnumerateObject().Any(property => property.Name.Equals("calibration", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Judge-enabled reports require captured calibration and judge metadata.");
        ValidateCases(report.Cases);
        ValidateRunMetadata(report.Label, report.RunNotes);
        if (report.Results.Any(result => result is null || !report.Cases.Any(item => item.Id == result.CaseId) ||
                result.Repetition < 1 || result.Repetition > report.Repeat || result.Checks is null || result.Review is null ||
                (result.Judge?.ContractValid == true && result.Issues is null)) ||
            report.Results.Select(result => (result.CaseId, result.Repetition)).Distinct().Count() != report.Results.Count)
            throw new InvalidDataException("Invalid evaluation results.");
        foreach (var result in report.Results) result.Review.Validate();
        if (report.CalibrationSamples.Length > 0) ValidateCalibrationSamples(report.CalibrationSamples);
        if (report.Calibration.Any(result => result is null || result.Call is null ||
                !report.CalibrationSamples.Any(sample => sample.HasSameContent(result.Sample)) ||
                (result.Call.ContractValid && result.Issues is null)) ||
            report.Calibration.Select(result => result.Sample.Id).Distinct().Count() != report.Calibration.Count)
            throw new InvalidDataException("Invalid calibration results.");
        if (report.Results.Select(result => result.Issues).Concat(report.Calibration.Select(result => result.Issues))
            .Any(issues => issues is not null && !HebrewJudge.ValidateIssues(issues)) ||
            report.Steps.Any(step => !double.IsFinite(step.ElapsedMilliseconds) || step.ElapsedMilliseconds < 0 ||
                step.InputTokens < 0 || step.OutputTokens < 0 || step.ReasoningTokens < 0 || step.CostCredits < 0))
            throw new InvalidDataException("Invalid findings or measurements.");
        return report;
    }

    /// <summary>Rejects invalid generic scenario expectations before preview or paid generation.</summary>
    public static void ValidateCases(EvaluationCase[] cases)
    {
        if (cases is not { Length: > 0 }) throw new InvalidDataException("Evaluation requires at least one case.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 100 ||
                item.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')) || !ids.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Prompt) || item.Prompt.Length > 4000 ||
                string.IsNullOrWhiteSpace(item.ReviewFocus) || item.ReviewFocus.Length > 1000 ||
                item.QuestionCount is < 1 or > 20 || item.Interaction is not ("single-choice" or "text-input" or "numeric-input") ||
                (item.ChoiceCount.HasValue && (item.Interaction != "single-choice" || item.ChoiceCount is < 2 or > 6)) ||
                item.MinPassageWords < 0 || item.MaxPassageWords < 0 || item.MinPassageWords > item.MaxPassageWords ||
                (item.UseMaximumQuestionCount && item.QuestionCount < 2))
                throw new InvalidDataException("Evaluation cases require unique safe IDs, bounded text and supported, consistent expectations.");
        }
    }

    /// <summary>Bounds optional developer metadata, preserving literal text without interpreting markup.</summary>
    public static void ValidateRunMetadata(string? label, string? notes)
    {
        if (label?.Length > 120 || notes?.Length > 4000 || label?.Any(char.IsControl) == true ||
            notes?.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')) == true)
            throw new InvalidDataException("Run metadata requires a label of at most 120 characters and plain-text notes of at most 4,000 characters.");
    }

    /// <summary>Rejects unusable expectations before a live run can spend calls on them.</summary>
    public static void ValidateCalibrationSamples(CalibrationSample[] samples)
    {
        if (samples is not { Length: > 0 } || samples.Any(sample => sample is null || string.IsNullOrWhiteSpace(sample.Id) || string.IsNullOrWhiteSpace(sample.Request) ||
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
