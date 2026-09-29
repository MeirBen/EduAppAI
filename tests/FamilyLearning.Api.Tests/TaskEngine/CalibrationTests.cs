using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class CalibrationTests
{
    private static readonly ExpectedHebrewIssue Expected = new("question", "להסיין");
    private static readonly CalibrationSample Sample = new("synthetic", "בקשת לימוד", [], [Expected]);
    private static HebrewIssue Issue(string path, string quote) => new(path, quote, "להסיק", "מילה שגויה", "invented-word");

    [Theory]
    [InlineData("question", "להסיין", true)]
    [InlineData("question", "אפשר להסיין מהקטע", true)]
    [InlineData("other-question", "להסיין", false)]
    [InlineData("question", "הסיין", false)]
    [InlineData("question", "מילה אחרת", false)]
    public void Calibration_requires_the_same_path_and_whole_offending_words(string path, string quote, bool matches)
    {
        var result = new CalibrationResult(Sample, new() { ContractValid = true }) { Issues = [Issue(path, quote)] };
        Assert.Equal(matches, result.Passed);
    }

    [Fact]
    public void Minimal_word_can_match_a_small_expected_phrase() =>
        Assert.True(new ExpectedHebrewIssue("question", "אפשר להסיין").Matches(Issue("question", "להסיין")));

    [Theory]
    [InlineData("אפשר להסיק שהנמלות יצאו", 1)]
    [InlineData("אפשר להסיין שהנמלים יצאו", 1)]
    [InlineData("אפשר להסיק שהנמלים יצאו", 0)]
    public void Quoting_two_defects_only_credits_the_ones_changed_by_the_suggestion(string suggestion, int missing)
    {
        var sample = Sample with { ExpectedIssues = [Expected, new("question", "שהנמלות")] };
        var result = new CalibrationResult(sample, new() { ContractValid = true })
        {
            Issues = [Issue("question", "אפשר להסיין שהנמלות יצאו") with { Suggestion = suggestion }]
        };
        Assert.Equal(missing, result.MissingExpectedIssueCount);
        Assert.Equal(missing == 0, result.Passed);
    }

    [Fact]
    public async Task Ant_control_accepts_a_correction_that_preserves_the_attached_prefix()
    {
        var controls = await EvaluationFiles.LoadFixtureAsync<CalibrationSample>("hebrew-review-samples.json");
        var sample = Assert.Single(controls.Items, sample => sample.Id == "reported-ants-defects");
        var issue = Issue("task.questions[1].prompt", "שהנמלות") with { Suggestion = "שהנמלים" };

        Assert.Contains(sample.ExpectedIssues, expected => expected.Matches(issue));
    }

    [Fact]
    public void Finding_expected_defects_does_not_excuse_unexpected_findings()
    {
        var result = new CalibrationResult(Sample, new() { ContractValid = true })
        { Issues = [Issue("question", "להסיין"), Issue("correct-field", "תקין")] };
        Assert.Equal(0, result.MissingExpectedIssueCount);
        Assert.Equal(1, result.UnexpectedFindingCount);
        Assert.False(result.Passed);
        Assert.True((result with { Sample = Sample with { AllowUnexpectedFindings = true } }).Passed);
    }

    [Fact]
    public void Clean_control_passes_only_with_a_valid_empty_review()
    {
        var result = new CalibrationResult(Sample with { ExpectedIssues = [] }, new() { ContractValid = true });
        Assert.False(result.Passed);
        result.Issues = [];
        Assert.True(result.Passed);
        result.Issues = [Issue("question", "להסיין")];
        Assert.False(result.Passed);
    }
}
