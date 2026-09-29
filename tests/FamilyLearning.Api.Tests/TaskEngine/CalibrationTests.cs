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
