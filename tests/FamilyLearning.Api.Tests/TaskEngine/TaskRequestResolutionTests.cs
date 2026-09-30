using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using static FamilyLearning.Api.Tests.TaskEngine.LearningPlanFixture;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class TaskRequestResolutionTests
{
    [Fact]
    public void Omitted_choices_resolve_once_and_collections_are_detached()
    {
        var plan = Reading();
        var resolved = Resolve(plan);
        Assert.Equal(120, resolved.Materials[0].Length!.Value);
        Assert.Equal("דמיון", resolved.Materials[0].Controls[0].Value.GetString());
        Assert.Equal("סיפור דמיוני", resolved.Materials[0].Controls[0].OptionMeaning);
        plan.Questions.Formats[0] = "numeric-input";
        Assert.Equal("text-input", resolved.Questions.Formats[0]);
        Assert.Equal(120, plan.Materials[0].Length!.Count!.Value);
    }

    [Fact]
    public void False_zero_and_empty_optional_text_survive_instead_of_defaults()
    {
        var plan = Numeric() with
        {
            Controls = [
            new(ControlId, "מתג", "boolean", "משמעות", Default: Json("true")),
            new(MaterialId, "מספר", "integer", "משמעות", Default: Json("5")),
            new(OtherId, "טקסט", "text", "משמעות", Default: Json("\"default\""))]
        };
        var values = Json($$$"""{"{{{ControlId}}}":false,"{{{MaterialId}}}":0,"{{{OtherId}}}":""}""");
        var resolved = Resolve(plan, new(plan.Defaults, ControlValues: values));
        Assert.False(resolved.Controls[0].Value.GetBoolean());
        Assert.Equal(0, resolved.Controls[1].Value.GetInt32());
        Assert.Equal("", resolved.Controls[2].Value.GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"unknown\":2}")]
    [InlineData("{\"22222222222222222222222222222222\":null}")]
    [InlineData("{\"22222222222222222222222222222222\":\"2\"}")]
    public void Invalid_control_maps_or_members_never_fall_back(string json)
    {
        var plan = Numeric() with { Controls = [new(ControlId, "מספר", "integer", "משמעות", Default: Json("2"))] };
        var result = TaskRequestResolver.Resolve(plan, new(plan.Defaults, ControlValues: Json(json)));
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Source_is_required_and_preserved_exactly()
    {
        var plan = Supplied("per-task");
        Assert.NotEmpty(TaskRequestResolver.Resolve(plan, new(plan.Defaults)).Errors);
        foreach (var value in new[] { "null", "\"  \"", "123" })
            Assert.NotEmpty(TaskRequestResolver.Resolve(plan, new(plan.Defaults,
                MaterialInputs: Json($$$"""{"{{{MaterialId}}}":{"sourceText":{{{value}}}}}"""))).Errors);
        var inputs = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object>
        { [MaterialId] = new { sourceText = Source } });
        Assert.Equal(Source, Resolve(plan, new(plan.Defaults, MaterialInputs: inputs)).Materials[0].Text);
        Assert.Equal(Source, Resolve(Supplied()).Materials[0].Text);
    }

    [Fact]
    public void Overrides_must_be_present_applicable_typed_and_adjustable()
    {
        var plan = Reading();
        TaskRequest[] invalid = [new(plan.Defaults, MaterialInputs: Json("null")),
            new(plan.Defaults, QuestionFormat: Json("null")), new(plan.Defaults, ChoiceCount: Json("2")),
            new(plan.Defaults, TotalWordCount: Json("120")),
            new(plan.Defaults, MaterialInputs: Json($$$"""{"{{{MaterialId}}}":null}""")),
            new(plan.Defaults, MaterialInputs: Json($$$"""{"{{{MaterialId}}}":{"wordCount":"120"}}""")),
            new(plan.Defaults, MaterialInputs: Json($$$"""{"{{{MaterialId}}}":{"wordCount":99}}""")),
            new(plan.Defaults, MaterialInputs: Json($$$"""{"{{{MaterialId}}}":{"sourceText":"source"}}""")),
            new(plan.Defaults, MaterialInputs: Json($$$"""{"{{{MaterialId}}}":{"lower":100}}"""))];
        foreach (var input in invalid) Assert.NotEmpty(TaskRequestResolver.Resolve(plan, input).Errors);
        var fixedPlan = plan with { Materials = [plan.Materials[0] with { Length = new("exact", new(120, false)) }] };
        Assert.NotEmpty(TaskRequestResolver.Resolve(fixedPlan, new(plan.Defaults,
            MaterialInputs: Json($$$"""{"{{{MaterialId}}}":{"wordCount":120}}"""))).Errors);
    }

    [Fact]
    public void Format_selection_resolves_uniform_choices_and_rejects_inapplicable_choice_override()
    {
        var plan = Mixed(true);
        var resolved = Resolve(plan, new(plan.Defaults, QuestionFormat: Json("\"numeric-input\"")));
        Assert.Equal(["numeric-input"], resolved.Questions.Formats);
        Assert.Null(resolved.Questions.ChoiceCount);
        Assert.NotEmpty(TaskRequestResolver.Resolve(plan, new(plan.Defaults,
            QuestionFormat: Json("\"numeric-input\""), ChoiceCount: Json("3"))).Errors);
        Assert.NotEmpty(TaskRequestResolver.Resolve(Mixed(), new(plan.Defaults,
            QuestionFormat: Json("\"numeric-input\""))).Errors);
    }

    [Fact]
    public void Chosen_counts_and_required_control_text_are_checked()
    {
        var plan = Numeric() with { Controls = [new(ControlId, "טקסט", "text", "משמעות", true)] };
        Assert.NotEmpty(TaskRequestResolver.Resolve(plan, new(plan.Defaults,
            ControlValues: Json($$$"""{"{{{ControlId}}}":"  "}"""))).Errors);
        Assert.NotEmpty(TaskRequestResolver.Resolve(Numeric(), new(plan.Defaults with { QuestionCount = int.MaxValue })).Errors);
        Assert.NotEmpty(TaskRequestResolver.Resolve(Mixed(), new(plan.Defaults with { QuestionCount = 2 })).Errors);
    }
    [Fact]
    public void Valid_overrides_resolve_to_effective_requirements_without_input_bounds()
    {
        var plan = Reading();
        var request = new TaskRequest(plan.Defaults, MaterialInputs: Json(
            "{\"11111111111111111111111111111111\":{\"wordCount\":140}}"));
        var resolved = Resolve(plan, request);
        Assert.Equal(new ResolvedLength("target", 140), resolved.Materials[0].Length);
        Assert.Equal(120, plan.Materials[0].Length!.Count!.Value);
        var choicePlan = Mixed(true);
        Assert.Equal(5, Resolve(choicePlan, new(choicePlan.Defaults, ChoiceCount: Json("5"))).Questions.ChoiceCount);
        var totalPlan = plan with { Materials = [plan.Materials[0] with { Length = null }], TotalLength = new("target", new(120, true)) };
        Assert.Equal(new ResolvedLength("target", 250), Resolve(totalPlan, new(totalPlan.Defaults, TotalWordCount: Json("250"))).TotalLength);
    }

    [Fact]
    public void Per_task_sources_share_the_aggregate_content_budget()
    {
        var plan = Supplied("per-task");
        plan = plan with { Materials = Enumerable.Range(1, 4).Select(i => plan.Materials[0] with { Id = i.ToString("x32") }).ToArray() };
        var sources = System.Text.Json.JsonSerializer.SerializeToElement(plan.Materials.ToDictionary(m => m.Id!, _ => new { sourceText = new string('א', 4000) }));
        var result = TaskRequestResolver.Resolve(plan, new(plan.Defaults, MaterialInputs: sources));
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
    }

}
