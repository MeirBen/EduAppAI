using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Shared validation for template defaults and the settings submitted for one task.</summary>
public static class TaskSettingsValidator
{
    /// <summary>Returns application-authored field errors; the path identifies the owning form section.</summary>
    public static Dictionary<string, string[]> Validate(TaskSettings? settings, string path = "settings")
    {
        var errors = new Dictionary<string, string[]>();
        if (settings is null)
        {
            errors[path] = ["יש לציין את הגדרות המשימה."];
            return errors;
        }
        if (string.IsNullOrWhiteSpace(settings.Topic) || settings.Topic.Length > 200)
            errors[$"{path}.topic"] = ["יש להזין נושא באורך של 1 עד 200 תווים."];
        if (string.IsNullOrWhiteSpace(settings.Audience) || settings.Audience.Length > 200)
            errors[$"{path}.audience"] = ["יש לתאר את קהל היעד באורך של 1 עד 200 תווים."];
        if (settings.Difficulty is not ("easy" or "medium" or "hard"))
            errors[$"{path}.difficulty"] = ["יש לבחור רמת קושי."];
        if (settings.QuestionCount < 1)
            errors[$"{path}.questionCount"] = ["מספר השאלות חייב להיות גדול מאפס."];
        return errors;
    }
}
