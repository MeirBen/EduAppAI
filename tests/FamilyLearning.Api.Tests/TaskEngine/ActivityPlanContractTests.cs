using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ActivityPlanContractTests
{
    private const string Source = "  מקור\nExact source  ";

    private static JsonObject Plan() => new()
    {
        ["schemaVersion"] = EngineVersions.SchemaVersion,
        ["name"] = "פעילות",
        ["goal"] = "למידה",
        ["guidance"] = "",
        ["documentGuidance"] = "",
        ["settings"] = new JsonObject { ["topic"] = "שפה", ["audience"] = "כיתה ג", ["difficulty"] = "easy", ["questionCount"] = 3 },
        ["materials"] = new JsonArray(new JsonObject
        {
            ["id"] = LearningPlanFixture.MaterialId,
            ["label"] = "מקור",
            ["source"] = "supplied",
            ["guidance"] = "",
            ["text"] = Source,
            ["length"] = null
        }),
        ["questions"] = new JsonObject
        {
            ["formats"] = new JsonArray("numeric-input", "text-input", "single-choice"),
            ["choiceCount"] = 2,
            ["guidance"] = ""
        },
        ["totalLength"] = null
    };

    [Fact]
    public void Concrete_activity_plan_accepts_verbatim_sources_and_all_required_formats()
    {
        var plan = Plan().Deserialize<LearningPlan>(EngineJson.Options)!;
        Assert.Empty(LearningPlanValidator.Validate(plan));
        Assert.Equal(Source, plan.Materials[0].Text);
        var wire = JsonSerializer.SerializeToNode(plan, EngineJson.Options)!;
        Assert.Equal(3, wire["settings"]!["questionCount"]!.GetValue<int>());
        Assert.Equal(2, wire["questions"]!["choiceCount"]!.GetValue<int>());
        Assert.False(wire.AsObject().ContainsKey("defaults"));
        Assert.False(wire.AsObject().ContainsKey("controls"));
    }

    [Theory]
    [InlineData("controls")]
    [InlineData("defaults")]
    [InlineData("input")]
    public void Retired_plan_fields_are_rejected_instead_of_ignored(string field)
    {
        var plan = Plan();
        plan[field] = new JsonObject();
        Assert.Throws<JsonException>(() => plan.Deserialize<LearningPlan>(EngineJson.Options));
    }

    [Fact]
    public void Missing_required_format_capacity_and_inapplicable_choice_count_are_rejected()
    {
        var node = Plan();
        node["settings"]!["questionCount"] = 2;
        Assert.Contains("settings.questionCount", LearningPlanValidator.Validate(node.Deserialize<LearningPlan>(EngineJson.Options)).Keys);
        node["settings"]!["questionCount"] = 3;
        node["questions"]!["formats"] = new JsonArray("numeric-input");
        Assert.Contains("questions.choiceCount", LearningPlanValidator.Validate(node.Deserialize<LearningPlan>(EngineJson.Options)).Keys);
    }
}
