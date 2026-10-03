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
        new(["numeric-input"], false, null, null, "", []), []);

    internal static LearningPlan Reading() => Numeric() with
    {
        Name = "קריאה",
        Goal = "הבנת הנקרא",
        Materials = [new(MaterialId, "סיפור", "generated", "עברית", null,
            new("target", new(120, true)),
            [new(ControlId, "סוג סיפור", "select", "בחר את סוג הסיפור", true,
                Json("\"דמיון\""), Options: [new("דמיון", "סיפור דמיוני"), new("עובדות")])])],
        Questions = new(["text-input"], false, null, null, "", [])
    };

    internal static LearningPlan Supplied(string source = "fixed") => Numeric() with
    {
        Materials = [new(MaterialId, "מקור", source, "", source == "fixed" ? Source : null, null, [])]
    };

    internal static LearningPlan Mixed(bool selectable = false) => Numeric(3) with
    {
        Questions = new(["numeric-input", "text-input", "single-choice"], selectable,
            selectable ? "single-choice" : null, new(3, true), "", [])
    };

    internal static ResolvedTaskRequest Resolve(LearningPlan plan, TaskRequest? input = null)
    {
        var result = TaskRequestResolver.Resolve(plan, input ?? new(plan.Defaults));
        Assert.Empty(result.Errors);
        return Assert.IsType<ResolvedTaskRequest>(result.Value);
    }
}
