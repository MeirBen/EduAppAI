using FamilyLearning.Api.TaskEngine;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class TaskRequestResolutionTests
{
    [Fact]
    public void Requirements_are_detached_and_optional_empty_guidance_is_preserved()
    {
        var plan = Reading();
        var resolved = Resolve(plan);
        plan.Questions.Formats[0] = "numeric-input";
        Assert.Equal("text-input", resolved.Questions.Formats[0]);
        Assert.Equal(120, resolved.Materials[0].Length!.Value);
        Assert.Equal("", resolved.Guidance);
        Assert.Equal(Source, Resolve(Supplied()).Materials[0].Text);
    }

    [Fact]
    public void Fingerprints_follow_effective_requirements_not_name_or_engine_revision()
    {
        var plan = Numeric();
        var original = Resolve(plan);
        Assert.Equal(TaskRequestResolver.Fingerprint(original), TaskRequestResolver.Fingerprint(Resolve(plan with { Name = "שם אחר" })));
        Assert.Equal(TaskRequestResolver.Fingerprint(original), TaskRequestResolver.Fingerprint(original with { EngineRevision = int.MaxValue }));
        Assert.NotEqual(TaskRequestResolver.Fingerprint(original), TaskRequestResolver.Fingerprint(Resolve(plan with { Settings = plan.Settings with { QuestionCount = 3 } })));
        Assert.NotEqual(TaskRequestResolver.Fingerprint(original), TaskRequestResolver.Fingerprint(Resolve(plan with { Questions = plan.Questions with { Guidance = "תרגול נוסף" } })));
    }

    [Fact]
    public void Missing_supplied_sources_and_impossible_counts_never_produce_partial_requirements()
    {
        var supplied = Supplied();
        foreach (var plan in new[] { supplied with { Materials = [supplied.Materials[0] with { Text = null }] },
            Numeric(int.MaxValue), Mixed() with { Settings = Numeric(2).Settings } })
        {
            var result = TaskRequestResolver.Resolve(plan);
            Assert.Null(result.Value);
            Assert.NotEmpty(result.Errors);
        }
    }
}
