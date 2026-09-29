using System.Text.Json;
using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class EvaluationPlanTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"evaluation-plan-{Guid.NewGuid():N}");

    [Fact]
    public async Task Basic_plan_does_not_load_judge_fixtures_and_preserves_case_order()
    {
        Directory.CreateDirectory(directory);
        var cases = new[] { Case("first"), Case("second") };
        await File.WriteAllTextAsync(Path.Combine(directory, "cases.json"), JsonSerializer.Serialize(cases));
        await File.WriteAllTextAsync(Path.Combine(directory, "hebrew-review-samples.json"), "broken");
        var request = new EvaluationRunRequest(["second", "first"], 1, false, 4, "baseline", "notes");
        var plan = await EvaluationPlan.LoadAsync(request, directory);
        Assert.Equal(new[] { "first", "second" }, plan.Cases.Select(item => item.Id));
        Assert.Equal(4, plan.PlannedCalls);
        Assert.Empty(plan.Controls);
        Assert.Empty(plan.CalibrationSha256);
        var report = plan.CreateReport([]);
        Assert.Equal("baseline", report.Label);
        Assert.Equal("notes", report.RunNotes);
        Assert.Equal(5, report.CallDelaySeconds);
        Assert.Empty(report.JudgePrompt);
        await Assert.ThrowsAsync<JsonException>(() => EvaluationPlan.LoadAsync(request with { Judge = true }, directory));
    }

    [Fact]
    public async Task Invalid_fixture_fails_during_planning_including_unselected_cases()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "cases.json"),
            JsonSerializer.Serialize(new[] { Case("selected"), Case("other") with { QuestionCount = 0 } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => EvaluationPlan.LoadAsync(new(["selected"], 1, false, 2), directory));
    }

    private static EvaluationCase Case(string id) => new(id, "prompt", "focus", 1, "text-input", null, null, null);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
