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
            LearningPlanFixture.Supplied(), LearningPlanFixture.Supplied("per-task"),
            LearningPlanFixture.Mixed(), LearningPlanFixture.Mixed(true)];
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
            plan with { Controls = [material.Controls[0]] },
            plan with { Materials = [material with { Text = "not generated" }] },
            plan with { Materials = [material with { Source = "fixed", Text = "source" }] },
            plan with { Materials = [material with { Length = new("exact", new(120, false)) }] },
            plan with { Materials = [material with { Length = new("range", Lower: 120, Upper: 120) }] },
            plan with { Materials = [material with { Length = new("range", Lower: 150, Upper: 100) }] },
            LearningPlanFixture.Mixed() with { Defaults = plan.Defaults },
            LearningPlanFixture.Mixed() with { Questions = LearningPlanFixture.Mixed().Questions with { ChoiceCount = new(7, false) } },
            LearningPlanFixture.Mixed(true) with { Questions = LearningPlanFixture.Mixed(true).Questions with { DefaultFormat = null } }
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
        Assert.Contains("defaults.questionCount", LearningPlanValidator.Validate(LearningPlanFixture.Numeric(21)).Keys);
        var select = new ControlDefinition(LearningPlanFixture.ControlId, "בחירה", "select", "משמעות",
            Options: Enumerable.Range(1, 21).Select(n => new ControlOption(n.ToString())).ToArray());
        Assert.NotEmpty(LearningPlanValidator.Validate(LearningPlanFixture.Numeric() with { Controls = [select] }));
        Assert.Empty(LearningPlanValidator.Validate(LearningPlanFixture.Numeric() with { Controls = [select with { Options = select.Options![..20] }] }));
    }

    [Fact]
    public void Compact_plan_budget_and_scoped_control_budget_are_enforced()
    {
        var plan = LearningPlanFixture.Numeric();
        var controls = Enumerable.Range(1, 16).Select(i => new ControlDefinition(i.ToString("x32"),
            new string('א', 100), "select", new string('ב', 500), Options:
            Enumerable.Range(1, 20).Select(n => new ControlOption(n.ToString(), new string('ג', 200))).ToArray())).ToArray();
        Assert.Contains("plan", LearningPlanValidator.Validate(plan with { Controls = controls }).Keys);
        Assert.NotEmpty(LearningPlanValidator.Validate(plan with { Controls = [.. controls, controls[0] with { Id = LearningPlanFixture.OtherId }] }));
    }
    [Fact]
    public void Total_length_feasibility_accounts_for_separate_material_bodies()
    {
        var plan = LearningPlanFixture.Numeric(1) with
        {
            Materials = Enumerable.Range(1, 4).Select(i => new MaterialDefinition(i.ToString("x32"),
                "חומר", "generated", "", null, null, [])).ToArray(),
            TotalLength = new("range", Lower: 4000, Upper: 4100)
        };
        // Four 1,999-character bodies plus a one-character title, prompt and answer fit in 7,999 characters.
        Assert.Empty(LearningPlanValidator.Validate(plan));
        Assert.NotEmpty(LearningPlanValidator.Validate(plan with { TotalLength = new("range", Lower: 4001, Upper: 4100) }));
    }

    [Fact]
    public void Scoped_controls_share_one_global_limit()
    {
        var plan = LearningPlanFixture.Reading();
        var controls = Enumerable.Range(4, 17).Select(i => new ControlDefinition(i.ToString("x32"), "בחירה", "boolean", "משמעות")).ToArray();
        plan = plan with { Controls = controls[..8], Materials = [plan.Materials[0] with { Controls = controls[8..] }] };
        Assert.NotEmpty(LearningPlanValidator.Validate(plan));
    }

}
