using System.Text.Json.Nodes;
using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class EvaluationRunStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"evaluation-store-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("directory")]
    [InlineData("run.json")]
    [InlineData("summary.json")]
    [InlineData("run.json.tmp")]
    [InlineData("summary.json.tmp")]
    public async Task Review_saves_reject_linked_artifacts_including_dangling_temporary_files(string link)
    {
        using var store = new EvaluationRunStore(Path.Combine(root, "runs"));
        var id = EvaluationRunStore.NewId();
        var directory = store.DirectoryFor(id);
        Directory.CreateDirectory(directory);
        var report = EvaluationReportsTests.CreateReport();
        report.Results.Add(new("reading", 1) { Authoring = EvaluationReportsTests.Step(), Generation = EvaluationReportsTests.Step() });
        await EvaluationFiles.SaveAsync(report, directory);
        var target = Path.Combine(root, "outside-artifacts");
        if (link == "directory")
        {
            Directory.Delete(directory, true);
            Directory.CreateSymbolicLink(directory, target);
        }
        else
        {
            File.Delete(Path.Combine(directory, link));
            File.CreateSymbolicLink(Path.Combine(directory, link), target);
        }
        await Assert.ThrowsAsync<IOException>(() => store.SaveReviewAsync(id, new("reading", 1, new() { Hebrew = 2 })));
        Assert.False(Path.Exists(target));
    }

    [Fact]
    public async Task Concurrent_reviews_preserve_other_results_and_refresh_summary()
    {
        using var store = new EvaluationRunStore(root);
        var id = EvaluationRunStore.NewId();
        var directory = store.DirectoryFor(id);
        Directory.CreateDirectory(directory);
        var report = EvaluationReportsTests.CreateReport(repeat: 2);
        for (var repetition = 1; repetition <= 2; repetition++)
            report.Results.Add(new("reading", repetition) { Authoring = EvaluationReportsTests.Step(), Generation = EvaluationReportsTests.Step() });
        await EvaluationFiles.SaveAsync(report, directory);
        await Task.WhenAll(
            store.SaveReviewAsync(id, new("reading", 1, new() { Hebrew = 1, Notes = "first" })),
            store.SaveReviewAsync(id, new("reading", 2, new() { Hebrew = 2, Notes = "second" })));
        var saved = await store.ReadAsync(id);
        Assert.Equal(new[] { "first", "second" }, saved.Results.Select(item => item.Review.Notes));
        var summary = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json")))!;
        Assert.Equal(1.5, summary["humanReview"]!["hebrew"]!["average"]!.GetValue<double>());
        Assert.False(File.Exists(Path.Combine(directory, "run.json.tmp")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
