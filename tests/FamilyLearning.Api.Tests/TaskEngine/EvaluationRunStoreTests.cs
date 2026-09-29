using System.Text.Json.Nodes;
using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class EvaluationRunStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"evaluation-store-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("directory")]
    [InlineData("run.json")]
    [InlineData("run.json.tmp")]
    public async Task Links_cannot_escape_the_artifact_root_including_dangling_write_targets(string link)
    {
        Directory.CreateDirectory(root);
        var id = EvaluationRunStore.NewId();
        var directory = Path.Combine(root, id);
        if (link == "directory") Directory.CreateSymbolicLink(directory, Path.Combine(root, "missing"));
        else
        {
            Directory.CreateDirectory(directory);
            File.CreateSymbolicLink(Path.Combine(directory, link), Path.Combine(root, "missing"));
        }
        using var store = new EvaluationRunStore(root);
        await Assert.ThrowsAsync<IOException>(() => store.ReadAsync(id));
        Assert.False(File.Exists(Path.Combine(root, "missing")));
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
