using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ActivityProbeCommandTests
{
    [Theory]
    [InlineData]
    [InlineData("--protocol", "edits")]
    public async Task Preview_never_creates_an_output_directory_or_requires_provider_configuration(params string[] protocol)
    {
        var output = Path.Combine(Path.GetTempPath(), "absent-activity-probe-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, await EvaluationCommand.Main(["--activity-contract-probe", .. protocol, "--output", output]));
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("--live")]
    [InlineData("--live", "--budget-usd", "0")]
    [InlineData("--live", "--budget-usd", "6.01")]
    [InlineData("--live", "--budget-usd", "NaN")]
    [InlineData("--live", "--budget-usd", "1", "--budget-usd", "2")]
    [InlineData("--judge")]
    [InlineData("--protocol", "other")]
    [InlineData("--protocol", "edits", "--protocol", "edits")]
    public async Task Missing_or_invalid_budget_and_extra_modes_fail_before_resolving_a_provider(params string[] args) =>
        Assert.Equal(2, await EvaluationCommand.Main(["--activity-contract-probe", .. args]));
}
