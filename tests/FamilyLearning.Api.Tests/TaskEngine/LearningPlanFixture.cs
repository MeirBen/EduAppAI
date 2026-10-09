using System.Text.Json;
using FamilyLearning.Api.TaskEngine;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Tests.TaskEngine;

internal static class LearningPlanFixture
{
    internal const string MaterialId = "11111111111111111111111111111111";
    internal const string ControlId = "22222222222222222222222222222222";
    internal const string OtherId = "33333333333333333333333333333333";
    internal const string Source = "\"שָׁלוֹם\" — Hello!\nDon't change בעלי־חיים.\n";

    internal static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    internal static LearningPlan Numeric(int count = 2) => new(
        "מספרים", "תרגול חשבון", "", new("חשבון", "כיתה ג", "medium", count), [],
        new(["numeric-input"], null, ""));

    internal static LearningPlan Reading() => Numeric() with
    {
        Name = "קריאה",
        Goal = "הבנת הנקרא",
        Materials = [new(MaterialId, "סיפור", "generated", "עברית", null, new("target", 120))],
        Questions = new(["text-input"], null, "")
    };

    internal static LearningPlan Supplied() => Numeric() with
    {
        Materials = [new(MaterialId, "מקור", "supplied", "", Source, null)]
    };

    internal static LearningPlan Mixed() => Numeric(3) with
    {
        Questions = new(["numeric-input", "text-input", "single-choice"], 3, "")
    };

    internal static LearningPlan Choices(int count = 3) => Numeric(count) with { Questions = new(["single-choice"], 3, "") };

    internal static ResolvedTaskRequest Resolve(LearningPlan plan)
    {
        var result = TaskRequestResolver.Resolve(plan);
        Assert.Empty(result.Errors);
        return Assert.IsType<ResolvedTaskRequest>(result.Value);
    }
}
