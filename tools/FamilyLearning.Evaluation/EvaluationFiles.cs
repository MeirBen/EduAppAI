using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Validation;

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

    /// <summary>Validates fixtures and hashes the same bytes, so changes during a run cannot alter its inputs.</summary>
    public static async Task<(T[] Items, string Sha256)> LoadFixtureAsync<T>(string name, string? directory = null)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory ?? AppContext.BaseDirectory, name));
        var items = JsonSerializer.Deserialize<T[]>(bytes, Json);
        if (items is not { Length: > 0 } || items.Any(item => item is null)) throw new InvalidDataException("Empty or null evaluation fixture.");
        if (items is EvaluationCase[] cases) ValidateCases(cases);
        if (items is CalibrationSample[] samples) ValidateCalibrationSamples(samples);
        return (items, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>Writes the authoritative checkpoint first and returns its persisted, derived summary.</summary>
    public static async Task<EvaluationSummary> SaveAsync(EvaluationReport report, string directory)
    {
        await WriteAsync("run.json", report);
        var summary = EvaluationSummary.Create(report);
        await WriteAsync("summary.json", summary);
        return summary;

        async Task WriteAsync<T>(string name, T value)
        {
            var path = Path.Combine(directory, name);
            // Deliberately uncancellable: retain partial results after Ctrl+C. A failed summary cannot corrupt the run.
            await using (var stream = File.Create(path + ".tmp"))
                await JsonSerializer.SerializeAsync(stream, value, Json);
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }

    /// <summary>Rejects unsupported reports and invalid human scores instead of inventing missing measurements.</summary>
    public static async Task<EvaluationReport> ReadReportAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("Evaluation report is too large.");
        using var document = await JsonDocument.ParseAsync(stream, new() { MaxDepth = Json.MaxDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("formatVersion", out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var version) ||
            version != EvaluationVersions.ReportFormat)
            throw new InvalidDataException($"Unsupported evaluation report; format version {EvaluationVersions.ReportFormat} is required.");
        var report = document.RootElement.Deserialize<EvaluationReport>(Json);
        if (report is null || report.FormatVersion != EvaluationVersions.ReportFormat || report.AutomaticChecksVersion < 1 ||
            report.Cases is not { Length: > 0 } || report.Repeat is < 1 or > 5 || report.Profile is null || report.CallDelaySeconds is < 0 or > 60 ||
            string.IsNullOrWhiteSpace(report.SuiteSha256) || report.Results is null || report.Calibration is null || report.CalibrationSamples is null ||
            report.Retries is null || report.Retries.Count > 100 || report.Retries.Any(retry => retry is null || retry.Call is null ||
                retry.Number is < 1 or > 3 || !double.IsFinite(retry.DelaySeconds) || retry.DelaySeconds is < 0 or > 300 ||
                !retry.Call.RequestSent || retry.Call.FinishedAtUtc is null || retry.Call.StatusCode != 429 || retry.Call.ContractValid))
            throw new InvalidDataException($"Invalid or unsupported evaluation report; format version {EvaluationVersions.ReportFormat} is required.");
        if (report.JudgeEnabled && (string.IsNullOrWhiteSpace(report.CalibrationSha256) || report.CalibrationSamples.Length == 0 ||
            string.IsNullOrWhiteSpace(report.JudgePromptVersion) || string.IsNullOrWhiteSpace(report.JudgePrompt) ||
            report.JudgeProfile is not { Count: > 0 }))
            throw new InvalidDataException("Judge-enabled reports require captured calibration and judge metadata.");
        ValidateCases(report.Cases);
        ValidateRunMetadata(report.Label, report.RunNotes);
        if (report.Results.Any(result => result is null || !report.Cases.Any(item => item.Id == result.CaseId) ||
                result.Repetition < 1 || result.Repetition > report.Repeat || result.Checks is null || result.Review is null ||
                result.Refinements is null || result.Replacements is null || result.Measurements is null ||
                result.PassageWordCount < 0 || result.RepeatedAnswerPosition is < 1 or > 6 ||
                (result.Judge?.ContractValid == true && result.Issues is null)) ||
            report.Results.Select(result => (result.CaseId, result.Repetition)).Distinct().Count() != report.Results.Count)
            throw new InvalidDataException("Invalid evaluation results.");
        foreach (var result in report.Results)
        {
            var scenario = report.Cases.Single(item => item.Id == result.CaseId);
            if (result.Refinements.Count != scenario.Refinements.Length || result.Replacements.Count != scenario.Replacements.Length ||
                result.Refinements.Any(step => step is null) || result.Replacements.Any(step => step is null))
                throw new InvalidDataException("Invalid stage evidence.");
            if (report.Status == "completed" && !HasCompleteWorkflow(scenario, result))
                throw new InvalidDataException("Completed trials require complete workflow evidence and justified stage skips.");
            result.Review.Validate();
        }
        if (report.CalibrationSamples.Length > 0) ValidateCalibrationSamples(report.CalibrationSamples);
        if (report.Calibration.Any(result => result is null || result.Call is null ||
                !report.CalibrationSamples.Any(sample => sample.HasSameContent(result.Sample)) ||
                (result.Call.ContractValid && result.Issues is null)) ||
            report.Calibration.Select(result => result.Sample.Id).Distinct().Count() != report.Calibration.Count)
            throw new InvalidDataException("Invalid calibration results.");
        if (report.Results.Select(result => result.Issues).Concat(report.Calibration.Select(result => result.Issues))
            .Any(issues => issues is not null && !HebrewJudge.ValidateIssues(issues)) ||
            report.Steps.Any(step => step.EngineRevision < 1 || step.SchemaVersion < 1 || step.Sources is null || step.Request is null ||
                step.Outcome is not ("pending" or "accepted" or "failed" or "skipped" or "clarification" or "cancelled" or "not-started") ||
                step.Role is not ("authoring" or "refinement" or "material-ideas" or "materials" or "material-polish" or "questions" or "replace-material" or "replace-question" or "review" or "calibration") ||
                step.Applied && !step.ContractValid || step.Outcome == "skipped" && (step.RequestSent || string.IsNullOrWhiteSpace(step.SkipReason)) ||
                !double.IsFinite(step.ElapsedMilliseconds) || step.ElapsedMilliseconds < 0 ||
                step.InputTokens < 0 || step.OutputTokens < 0 || step.ReasoningTokens < 0 || step.CostCredits < 0))
            throw new InvalidDataException("Invalid findings or measurements.");
        return report;
    }

    /// <summary>Checks execution evidence, independently of judge availability or whether the trial passed.</summary>
    internal static bool HasCompleteWorkflow(EvaluationCase scenario, EvaluationResult result)
    {
        if (result.Refinements.Count != scenario.Refinements.Length || result.Replacements.Count != scenario.Replacements.Length)
            return false;
        var ready = scenario.InitialPlan is not null;
        if (ready)
        {
            if (!Skipped(result.Authoring, "authoring", "fixed-plan")) return false;
        }
        else
        {
            var stopped = false;
            foreach (var (step, index) in new[] { result.Authoring }.Concat(result.Refinements).Select((step, index) => (step, index)))
            {
                var role = index == 0 ? "authoring" : "refinement";
                if (stopped)
                {
                    if (!Skipped(step, role, "earlier-stage")) return false;
                    continue;
                }
                if (!Finished(step, role)) return false;
                ready = step!.Applied;
                stopped = !step.ContractValid;
            }
        }
        if (result.Input is { } input && (input.Materials is null || input.Materials.Any(material => material is null))) return false;
        if (ready && result.Input is null)
        {
            if (result.Checks.GetValueOrDefault("inputResolution", true)) return false;
            ready = false;
        }
        if (!ready)
        {
            if (!Skipped(result.MaterialIdeas, "material-ideas", "earlier-stage") ||
                !Skipped(result.Materials, "materials", "earlier-stage") ||
                !Skipped(result.MaterialPolish, "material-polish", "earlier-stage")) return false;
        }
        else if (!result.Input!.Materials.Any(material => material.Source == "generated"))
        {
            if (!Skipped(result.MaterialIdeas, "material-ideas", "no-generated-materials") ||
                !Skipped(result.Materials, "materials", "no-generated-materials") ||
                !Skipped(result.MaterialPolish, "material-polish", "no-generated-materials")) return false;
        }
        else
        {
            if (!Finished(result.MaterialIdeas, "material-ideas")) return false;
            ready = result.MaterialIdeas!.Applied;
            if (ready)
            {
                if (result.SelectedMaterialIdea is null || !Finished(result.Materials, "materials")) return false;
                ready = result.Materials!.Applied;
            }
            else if (!Skipped(result.Materials, "materials", "earlier-stage")) return false;
            if (ready)
            {
                if (!Finished(result.MaterialPolish, "material-polish")) return false;
                ready = result.MaterialPolish!.Applied;
            }
            else if (!Skipped(result.MaterialPolish, "material-polish", "earlier-stage")) return false;
        }
        if (!ready)
        {
            if (!Skipped(result.Generation, "questions", "earlier-stage")) return false;
        }
        else
        {
            if (!Finished(result.Generation, "questions")) return false;
            ready = result.Generation!.Applied;
        }
        for (var index = 0; index < result.Replacements.Count; index++)
        {
            var step = result.Replacements[index];
            var replacement = scenario.Replacements[index];
            if (!ready)
            {
                if (!Skipped(step, replacement.Stage, "earlier-stage")) return false;
                continue;
            }
            var targetCount = replacement.Stage == "replace-material" ? result.Document?.Materials?.Length : result.Document?.Questions?.Length;
            if (targetCount is { } count && replacement.TargetIndex >= count)
            {
                if (!Skipped(step, replacement.Stage, "missing-target")) return false;
                ready = false;
            }
            else
            {
                if (!Finished(step, replacement.Stage)) return false;
                ready = step.Applied;
            }
        }
        return true;

        static bool Skipped(EvaluationStep? step, string role, string reason) => step is not null && step.Role == role &&
            step.Outcome == "skipped" && step.SkipReason == reason && !step.RequestSent && !step.ResponseReceived && !step.Applied;

        static bool Finished(EvaluationStep? step, string role) => step is not null && step.Role == role && step.FinishedAtUtc.HasValue &&
            (step.Outcome == "failed" ? !step.ContractValid && !step.Applied && step.Failure is not null :
                step.ContractValid && step.RequestSent && step.ResponseReceived && step.Output is not null && step.Request is { Length: > 0 } &&
                step.Schema.HasValue && step.RequestSha256 is not null && step.SchemaSha256 is not null &&
                (step.Outcome == "accepted" && step.Applied || step.Outcome == "clarification" && !step.Applied && role is "authoring" or "refinement"));
    }

    /// <summary>Rejects invalid generic scenario expectations before preview or paid generation.</summary>
    public static void ValidateCases(EvaluationCase[] cases)
    {
        if (cases is not { Length: > 0 }) throw new InvalidDataException("Evaluation requires at least one case.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var limits = ContentLimits.Current;
        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 100 ||
                item.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')) || !ids.Add(item.Id) ||
                (item.InitialPlan is null ? string.IsNullOrWhiteSpace(item.Prompt) || item.Prompt.Length > limits.MessageLength : !string.IsNullOrEmpty(item.Prompt)) ||
                item.Refinements is not { Length: <= 3 } || item.Refinements.Any(message => string.IsNullOrWhiteSpace(message) || message.Length > limits.MessageLength) ||
                item.InitialPlan is not null && item.Refinements.Length > 0 || item.ExpectedGeneratedMaterials < 0 || item.ExpectedGeneratedMaterials > limits.MaxMaterials ||
                item.Replacements is not { Length: <= 8 } || item.Replacements.Any(replacement => replacement is null ||
                    replacement.Stage is not ("replace-material" or "replace-question") || replacement.TargetIndex < 0 ||
                    replacement.TargetIndex >= (replacement.Stage == "replace-material" ? limits.MaxMaterials : item.QuestionCount) ||
                    replacement.Instruction?.Length > limits.MessageLength) ||
                item.ExpectedLength is { } length && !ValidLength(length, limits.ContentLength) ||
                string.IsNullOrWhiteSpace(item.ReviewFocus) || item.ReviewFocus.Length > 1000 ||
                item.QuestionCount < 1 || item.Interaction is not ("single-choice" or "text-input" or "numeric-input") ||
                item.AdditionalControlCount < 0 || item.AdditionalControlCount > limits.MaxControls ||
                (item.ChoiceCount.HasValue && (item.Interaction != "single-choice" || item.ChoiceCount < limits.MinChoiceCount || item.ChoiceCount > limits.MaxChoiceCount)) ||
                item.MinPassageWords < 0 || item.MaxPassageWords < 0 || item.MinPassageWords > item.MaxPassageWords ||
                (item.SettingsOverride is { } settings &&
                    (TaskSettingsValidator.Validate(settings).Count > 0 || settings.QuestionCount != item.QuestionCount)))
                throw new InvalidDataException("Evaluation cases require unique safe IDs, bounded text and supported, consistent expectations.");
            if (item.InitialPlan is { } plan)
            {
                if (item.InitialInput is not null && item.SettingsOverride is not null)
                    throw new InvalidDataException("Fixed input and settings override cannot both be supplied.");
                var resolved = TaskRequestResolver.Resolve(plan, item.InitialInput ?? new(item.SettingsOverride ?? plan.Defaults));
                if (resolved.Value is not { } input || input.Settings.QuestionCount != item.QuestionCount ||
                    !input.Questions.Formats.Contains(item.Interaction) || item.ChoiceCount != input.Questions.ChoiceCount)
                    throw new InvalidDataException("Fixed-plan expectations must match valid effective input.");
            }
            else if (item.InitialInput is not null) throw new InvalidDataException("Initial input requires a fixed plan.");
        }
    }

    /// <summary>A word count cannot exceed the content's character limit.</summary>
    private static bool ValidLength(Api.TaskEngine.Models.ResolvedLength length, int maxWords) => length.Mode switch
    {
        "target" => length.Value > 0 && length.Value <= maxWords && length.Lower is null && length.Upper is null,
        "range" => length.Value is null && length.Lower > 0 && length.Upper > length.Lower && length.Upper <= maxWords,
        _ => false
    };

    /// <summary>Bounds optional developer metadata, preserving literal text without interpreting markup.</summary>
    public static void ValidateRunMetadata(string? label, string? notes)
    {
        if (label?.Length > 120 || notes?.Length > 4000 || label?.Any(char.IsControl) == true ||
            notes?.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')) == true)
            throw new InvalidDataException("Run metadata requires a label of at most 120 characters and plain-text notes of at most 4,000 characters.");
    }

    /// <summary>Rejects unusable expectations before a live run can spend calls on them.</summary>
    private static void ValidateCalibrationSamples(CalibrationSample[] samples)
    {
        if (samples is not { Length: > 0 } || samples.Any(sample => sample is null || string.IsNullOrWhiteSpace(sample.Id) || string.IsNullOrWhiteSpace(sample.Request) ||
            sample.Texts is not { Length: > 0 } || sample.ExpectedIssues is null || sample.ExpectedIssues.Length > 20 ||
            sample.Texts.Any(text => text is null || string.IsNullOrWhiteSpace(text.Path) || string.IsNullOrWhiteSpace(text.Text)) ||
            sample.Texts.Select(text => text.Path).Distinct().Count() != sample.Texts.Length ||
            sample.ExpectedIssues.Any(expected => expected is null || string.IsNullOrWhiteSpace(expected.Quote) ||
                !sample.Texts.Any(text => text.Path == expected.Path && text.Text.Contains(expected.Quote, StringComparison.Ordinal))) ||
            sample.ExpectedIssues.Distinct().Count() != sample.ExpectedIssues.Length) ||
            samples.Select(sample => sample.Id).Distinct().Count() != samples.Length || samples.All(sample => sample.Advisory))
            throw new InvalidDataException("Calibration requires a gating control, unique fields and expectations quoted from those fields.");
    }
}
