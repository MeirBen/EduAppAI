using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class PlanChangesTests
{
    [Fact]
    public void Normalized_plans_own_nested_collections_and_json_defaults()
    {
        var previous = Reading();
        LearningPlan canonical;
        using (var defaults = System.Text.Json.JsonDocument.Parse("\"דמיון\""))
        {
            previous.Materials[0].Controls[0] = previous.Materials[0].Controls[0] with { Default = defaults.RootElement };
            canonical = PlanChanges.AssignNewIds(previous, previous);
        }
        previous.Questions.Formats[0] = "numeric-input";
        previous.Materials[0].Controls[0].Options![0] = new("changed");
        previous.Materials[0].Controls[0] = previous.Materials[0].Controls[0] with { Label = "changed" };

        Assert.Equal("text-input", canonical.Questions.Formats[0]);
        Assert.Equal("סוג סיפור", canonical.Materials[0].Controls[0].Label);
        Assert.Equal("דמיון", canonical.Materials[0].Controls[0].Options![0].Value);
        Assert.Equal("דמיון", canonical.Materials[0].Controls[0].Default!.Value.GetString());
        Assert.Empty(LearningPlanValidator.Validate(canonical));
    }

    [Fact]
    public void New_ids_are_assigned_only_to_nulls_without_mutating_the_proposal()
    {
        var proposal = Reading();
        proposal = proposal with
        {
            Materials = [proposal.Materials[0] with { Id = null,
            Controls = [proposal.Materials[0].Controls[0] with { Id = null }] }]
        };
        var canonical = PlanChanges.AssignNewIds(proposal, null);
        Assert.Empty(LearningPlanValidator.Validate(canonical));
        Assert.Matches("^[0-9a-f]{32}$", canonical.Materials[0].Id!);
        Assert.NotEqual(canonical.Materials[0].Id, canonical.Materials[0].Controls[0].Id);
        Assert.Null(proposal.Materials[0].Id);
        Assert.Throws<TaskValidationException>(() => PlanChanges.AssignNewIds(Reading(), null));
    }

    [Fact]
    public void Renaming_and_moving_retain_identity_and_report_actual_ordered_changes()
    {
        var previous = Reading();
        var proposal = previous with
        {
            Materials = [previous.Materials[0] with { Label = "חדש", Controls = [] }],
            Controls = [previous.Materials[0].Controls[0] with { Label = "תווית חדשה" }]
        };
        var normalized = PlanChanges.AssignNewIds(proposal, previous);
        Assert.Equal(MaterialId, normalized.Materials[0].Id);
        Assert.Equal(ControlId, normalized.Controls[0].Id);
        var changes = PlanChanges.Compare(previous, normalized);
        Assert.Contains(changes, change => change.Kind == "moved" && change.Id == ControlId);
        Assert.Contains(changes, change => change.Kind == "changed" && change.Id == MaterialId);
        Assert.Empty(PlanChanges.Compare(previous, PlanChanges.AssignNewIds(previous, previous)));
        Assert.Contains(PlanChanges.Compare(previous, Numeric()), change => change.Kind == "removed" && change.Id == MaterialId);
        Assert.Equal(changes, PlanChanges.Compare(previous, normalized));
    }

    [Fact]
    public void Unknown_duplicate_and_wrong_category_ids_fail_without_label_matching()
    {
        var previous = Reading();
        LearningPlan[] invalid = [
            previous with { Materials = [previous.Materials[0] with { Id = OtherId }] },
            previous with { Controls = [previous.Materials[0].Controls[0]] },
            previous with { Materials = [previous.Materials[0] with { Id = ControlId, Controls = [] }],
                Controls = [previous.Materials[0].Controls[0] with { Id = MaterialId }] }];
        foreach (var proposal in invalid) Assert.Throws<TaskValidationException>(() => PlanChanges.AssignNewIds(proposal, previous));
        var replacement = previous with { Materials = [previous.Materials[0] with { Id = null, Controls = [] }] };
        Assert.NotEqual(MaterialId, PlanChanges.AssignNewIds(replacement, previous).Materials[0].Id);
    }

    [Fact]
    public void Retained_fixed_source_cannot_change_text_or_source_kind_through_authoring()
    {
        var previous = Supplied();
        foreach (var material in new[] { previous.Materials[0] with { Text = "rewritten" },
                     previous.Materials[0] with { Source = "per-task", Text = null } })
            Assert.Throws<TaskValidationException>(() => PlanChanges.AssignNewIds(previous with { Materials = [material] }, previous));
        Assert.Equal(Source, PlanChanges.AssignNewIds(previous, previous).Materials[0].Text);
    }
}
