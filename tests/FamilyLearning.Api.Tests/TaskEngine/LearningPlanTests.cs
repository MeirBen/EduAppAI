using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class LearningPlanTests
{
    [Fact]
    public void Canonical_plans_cover_generated_supplied_question_only_mixed_and_selectable_work()
    {
        LearningPlan[] plans = [LearningPlanFixture.Reading(), LearningPlanFixture.Numeric(20),
            LearningPlanFixture.Supplied(), LearningPlanFixture.Mixed()];
        foreach (var plan in plans) Assert.Empty(LearningPlanValidator.Validate(plan));
        Assert.Contains("schemaVersion", LearningPlanValidator.Validate(plans[0] with { SchemaVersion = EngineVersions.SchemaVersion + 1 }).Keys);
    }

    [Fact]
    public void Invalid_id_source_length_and_choice_rules_are_rejected()
    {
        var plan = LearningPlanFixture.Reading();
        var material = plan.Materials[0];
        LearningPlan[] invalid = [
            plan with { Materials = [material with { Id = null }] },
            plan with { Materials = [material with { Text = "not generated" }] },
            plan with { Materials = [material with { Source = "fixed", Text = "source" }] },
            plan with { Materials = [material with { Length = new("exact", 120) }] },
            plan with { Materials = [material with { Length = new("range", Lower: 120, Upper: 120) }] },
            plan with { Materials = [material with { Length = new("range", Lower: 150, Upper: 100) }] },
            LearningPlanFixture.Mixed() with { Settings = plan.Settings },
            LearningPlanFixture.Mixed() with { Questions = LearningPlanFixture.Mixed().Questions with { ChoiceCount = 7 } },
            plan with { DocumentGuidance = new string('א', 1001) },
        ];
        foreach (var value in invalid) Assert.NotEmpty(LearningPlanValidator.Validate(value));
    }

    [Fact]
    public void Impossible_counts_and_sources_fail_without_count_sized_allocations()
    {
        Assert.NotEmpty(LearningPlanValidator.Validate(LearningPlanFixture.Numeric(int.MaxValue)));
        var plan = LearningPlanFixture.Supplied();
        plan = plan with
        {
            Materials = Enumerable.Range(1, 4).Select(i => plan.Materials[0] with
            { Id = i.ToString("x32"), Text = new string('א', 4000) }).ToArray()
        };
        Assert.NotEmpty(LearningPlanValidator.Validate(plan));
    }

    [Fact]
    public void Question_count_and_option_limits_belong_to_the_validator()
    {
        Assert.Contains("settings.questionCount", LearningPlanValidator.Validate(LearningPlanFixture.Numeric(21)).Keys);

    }


    [Fact]
    public void Total_length_feasibility_accounts_for_separate_material_bodies()
    {
        var plan = LearningPlanFixture.Numeric(1) with
        {
            Materials = Enumerable.Range(1, 4).Select(i => new MaterialDefinition(i.ToString("x32"),
                "חומר", "generated", "", null, null)).ToArray(),
            TotalLength = new("range", Lower: 4000, Upper: 4100)
        };
        // Four 1,999-character bodies plus a one-character title, prompt and answer fit in 7,999 characters.
        Assert.Empty(LearningPlanValidator.Validate(plan));
        Assert.NotEmpty(LearningPlanValidator.Validate(plan with { TotalLength = new("range", Lower: 4001, Upper: 4100) }));
    }



}
