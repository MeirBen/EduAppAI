using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class EvaluationValidationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"learning-validation-{Guid.NewGuid():N}");
    private static readonly EvaluationCase ValidCase = new("reading-1", "בקשת לימוד", "התאמה לגיל", 2, "text-input", null, null, null);

    [Theory]
    [InlineData("id-unsafe")]
    [InlineData("prompt-empty")]
    [InlineData("focus-long")]
    [InlineData("questions-zero")]
    [InlineData("interaction")]
    [InlineData("choices-small")]
    [InlineData("passage-inverted")]
    [InlineData("settings-difficulty")]
    [InlineData("count-override-mismatch")]
    [InlineData("parameters-too-many")]
    [InlineData("both-plan-and-prompt")]
    [InlineData("too-many-refinements")]
    [InlineData("replacement-target")]
    [InlineData("length-mode")]
    public async Task Malformed_case_fixture_is_rejected_when_loaded(string invalid)
    {
        var scenario = invalid switch
        {
            "id-unsafe" => ValidCase with { Id = "../reading" },
            "prompt-empty" => ValidCase with { Prompt = " " },
            "focus-long" => ValidCase with { ReviewFocus = new('א', 1001) },
            "questions-zero" => ValidCase with { QuestionCount = 0 },
            "interaction" => ValidCase with { Interaction = "essay" },
            "choices-small" => ValidCase with { Interaction = "single-choice", ChoiceCount = 1 },
            "passage-inverted" => ValidCase with { MinPassageWords = 20, MaxPassageWords = 10 },
            "settings-difficulty" => ValidCase with { SettingsOverride = LearningPlanFixture.Numeric().Defaults with { Difficulty = "unknown" } },
            "count-override-mismatch" => ValidCase with { SettingsOverride = LearningPlanFixture.Numeric(3).Defaults },
            "parameters-too-many" => ValidCase with { AdditionalControlCount = 17 },
            "both-plan-and-prompt" => ValidCase with { InitialPlan = LearningPlanFixture.Numeric() },
            "too-many-refinements" => ValidCase with { Refinements = ["א", "ב", "ג", "ד"] },
            "replacement-target" => ValidCase with { Replacements = [new("replace-question", 2)] },
            _ => ValidCase with { ExpectedLength = new("guess", 120) }
        };
        var path = await WriteFixtureAsync([scenario]);

        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.LoadFixtureAsync<EvaluationCase>(path));
    }

    [Fact]
    public async Task Duplicate_case_ids_are_rejected_when_loaded()
    {
        var path = await WriteFixtureAsync([ValidCase, ValidCase with { Prompt = "בקשה אחרת" }]);
        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.LoadFixtureAsync<EvaluationCase>(path));
    }

    [Theory]
    [InlineData("single-choice", 6)]
    public async Task Supported_case_expectations_are_preserved(string interaction, int? choices)
    {
        var scenario = ValidCase with
        {
            Id = new('a', 100),
            Prompt = new('א', 4000),
            ReviewFocus = new('א', 1000),
            Interaction = interaction,
            ChoiceCount = choices,
            AdditionalControlCount = 16,
            QuestionCount = 20,
            SettingsOverride = LearningPlanFixture.Numeric(20).Defaults,
            MinPassageWords = 0,
            MaxPassageWords = 0
        };
        var path = await WriteFixtureAsync([scenario]);

        var loaded = await EvaluationFiles.LoadFixtureAsync<EvaluationCase>(path);

        Assert.Equal(JsonSerializer.Serialize(scenario), JsonSerializer.Serialize(Assert.Single(loaded.Items)));
        Assert.Equal(64, loaded.Sha256.Length);
    }

    [Fact]
    public async Task Nonjudge_report_can_omit_calibration_metadata()
    {
        var path = await WriteReportAsync(false);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        foreach (var property in new[] { "calibrationSha256", "calibrationSamples", "judgePromptVersion", "judgePrompt" })
            json.Remove(property);
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var loaded = await EvaluationFiles.ReadReportAsync(path);

        Assert.False(loaded.JudgeEnabled);
        Assert.Empty(loaded.CalibrationSamples);
        Assert.Empty(loaded.Calibration);
        Assert.Equal(2, loaded.PlannedCalls);
    }

    [Fact]
    public async Task Calibration_fixture_rejects_expectations_that_are_not_quoted_from_its_text()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "controls.json");
        CalibrationSample[] samples = [new("control", "בקשת לימוד", [new("question", "מילה תקינה")], [new("question", "מילה חסרה")])];
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(samples));

        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.LoadFixtureAsync<CalibrationSample>(path));
    }

    [Theory]
    [InlineData("calibrationSha256")]
    [InlineData("calibrationSamples")]
    [InlineData("judgePromptVersion")]
    [InlineData("judgePrompt")]
    [InlineData("calibration")]
    public async Task Judge_report_requires_each_captured_calibration_member(string property)
    {
        var path = await WriteReportAsync(true);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json.Remove(property);
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var error = await Record.ExceptionAsync(() => EvaluationFiles.ReadReportAsync(path));

        Assert.True(error is InvalidDataException or JsonException, $"Expected report validation failure, got {error?.GetType().Name ?? "success"}.");
    }

    [Theory]
    [InlineData("path", "")]
    [InlineData("quote", " ")]
    [InlineData("suggestion", "להסיין")]
    [InlineData("reason", "long")]
    [InlineData("kind", "style")]
    public async Task Stored_findings_validate_all_fields_for_generated_and_calibration_reviews(string field, string value)
    {
        foreach (var target in new[] { "results", "calibration" })
        {
            var path = await WriteReportAsync(true);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var issue = JsonSerializer.SerializeToNode(new HebrewIssue("question", "להסיין", "להסיק", "מילה שגויה", "spelling"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            issue[field] = value == "long" ? new string('א', field == "path" ? 201 : 501) : value;
            json[target]![0]!["issues"] = new JsonArray(issue);
            await File.WriteAllTextAsync(path, json.ToJsonString());

            await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
        }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    [InlineData("null-item")]
    public async Task Stored_findings_reject_invalid_collections(string invalid)
    {
        foreach (var target in new[] { "results", "calibration" })
        {
            var path = await WriteReportAsync(true);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var issues = new JsonArray();
            if (invalid == "null-item") issues.Add(null);
            else
                for (var index = 0; index < (invalid == "duplicate" ? 2 : 21); index++)
                    issues.Add(JsonSerializer.SerializeToNode(new HebrewIssue(invalid == "duplicate" ? "question" : $"question{index}",
                        "להסיין", "להסיק", "מילה שגויה", "spelling"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            json[target]![0]!["issues"] = issues;
            await File.WriteAllTextAsync(path, json.ToJsonString());

            await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
        }
    }

    [Fact]
    public async Task Human_review_notes_are_bounded_when_reading_reports()
    {
        var path = await WriteReportAsync(false);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["results"]![0]!["review"]!["notes"] = new string('א', 4001);
        await File.WriteAllTextAsync(path, json.ToJsonString());

        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
    }

    [Fact]
    public async Task Optional_run_metadata_round_trips_as_text_without_changing_comparability()
    {
        var path = await WriteReportAsync(false);
        var baseline = await EvaluationFiles.ReadReportAsync(path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var label = "Qwen <baseline> — בדיקה";
        var notes = "<script>alert('plain text')</script>\nבדיקת שפה";
        json["label"] = label;
        json["runNotes"] = notes;
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var candidate = await EvaluationFiles.ReadReportAsync(path);
        await EvaluationFiles.SaveAsync(candidate, directory);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;

        Assert.Equal(label, saved["label"]!.GetValue<string>());
        Assert.Equal(notes, saved["runNotes"]!.GetValue<string>());
        Assert.True(EvaluationComparison.Compare(baseline, candidate).DirectlyComparable);
    }

    [Theory]
    [InlineData("passageWordCount", -1)]
    [InlineData("repeatedAnswerPosition", 0)]
    [InlineData("repeatedAnswerPosition", 7)]
    public async Task Report_rejects_impossible_content_measurements(string property, int value)
    {
        var path = await WriteReportAsync(false);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["results"]![0]![property] = value;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
    }

    [Theory]
    [InlineData("label", 121)]
    [InlineData("runNotes", 4001)]
    [InlineData("label", -1)]
    [InlineData("runNotes", -1)]
    public async Task Run_metadata_limits_are_shared_by_files_and_ui(string property, int length)
    {
        var value = length < 0 ? "invalid\0text" : new string('א', length);
        Assert.Throws<InvalidDataException>(() => EvaluationFiles.ValidateRunMetadata(
            property == "label" ? value : null, property == "runNotes" ? value : null));
        var path = await WriteReportAsync(false);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json[property] = value;
        await File.WriteAllTextAsync(path, json.ToJsonString());

        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationFiles.ReadReportAsync(path));
    }

    [Fact]
    public void Shared_review_validation_preserves_rubric_and_note_boundary()
    {
        new ManualReview { Hebrew = 0, Correctness = 1, AgeFit = 2, Notes = new('א', 4000) }.Validate();
        Assert.Throws<InvalidDataException>(() => new ManualReview { Consistency = -1 }.Validate());
        Assert.Throws<InvalidDataException>(() => new ManualReview { AnswerClarity = 3 }.Validate());
        Assert.Throws<InvalidDataException>(() => new ManualReview { Notes = new('א', 4001) }.Validate());
        Assert.Throws<InvalidDataException>(() => new ManualReview { CorrectionCount = -1 }.Validate());
        Assert.Throws<InvalidDataException>(() => new ManualReview { TimeToReadySeconds = double.NaN }.Validate());
        Assert.Throws<InvalidDataException>(() => new ManualReview { TimeToReadySeconds = 604801 }.Validate());
        EvaluationFiles.ValidateRunMetadata(new('א', 120), new('א', 4000));
        EvaluationFiles.ValidateRunMetadata(null, "\r\n\tPlain text");
    }

    [Fact]
    public void Shared_finding_validation_accepts_bounded_findings_and_rejects_missing_review()
    {
        Assert.False(HebrewJudge.ValidateIssues(null));
        Assert.True(HebrewJudge.ValidateIssues([]));
        var issues = Enumerable.Range(0, 20).Select(index => new HebrewIssue(index.ToString().PadRight(200, 'a'),
            new('א', 500), new('ב', 500), new('ג', 500), "grammar-syntax")).ToArray();
        Assert.True(HebrewJudge.ValidateIssues(issues));
    }

    [Fact]
    public async Task Current_suite_and_isolated_fixture_directory_are_supported()
    {
        var suite = await EvaluationFiles.LoadFixtureAsync<EvaluationCase>("cases.json");
        await WriteFixtureAsync(suite.Items);

        var isolated = await EvaluationFiles.LoadFixtureAsync<EvaluationCase>("cases.json", directory);

        Assert.Equal(JsonSerializer.Serialize(suite.Items), JsonSerializer.Serialize(isolated.Items));
    }

    private async Task<string> WriteFixtureAsync(EvaluationCase[] cases)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "cases.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cases));
        return path;
    }

    private async Task<string> WriteReportAsync(bool judge)
    {
        Directory.CreateDirectory(directory);
        var report = EvaluationReportsTests.CreateReport(judge: judge);
        report.Results.Add(new("reading", 1)
        {
            Plan = EvaluationFixtures.Plan(),
            Input = LearningPlanFixture.Resolve(EvaluationFixtures.Plan()),
            Materials = EvaluationReportsTests.Skipped(),
            Authoring = EvaluationReportsTests.Step(),
            Generation = EvaluationReportsTests.Step(role: "questions"),
            Judge = judge ? EvaluationReportsTests.Step() : null,
            Issues = judge ? [] : null
        });
        if (judge) report.Calibration.Add(new(report.CalibrationSamples[0], EvaluationReportsTests.Step()) { Issues = [] });
        await EvaluationFiles.SaveAsync(report, directory);
        return Path.Combine(directory, "run.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
