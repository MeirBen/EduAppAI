using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Validates the concrete settings owned by an activity plan.</summary>
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
        if (string.IsNullOrWhiteSpace(settings.Topic) || settings.Topic.Length > EngineValidation.SettingTextLength)
            errors[$"{path}.topic"] = [$"יש להזין נושא באורך של 1 עד {EngineValidation.SettingTextLength} תווים."];
        if (string.IsNullOrWhiteSpace(settings.Audience) || settings.Audience.Length > EngineValidation.SettingTextLength)
            errors[$"{path}.audience"] = [$"יש לתאר את קהל היעד באורך של 1 עד {EngineValidation.SettingTextLength} תווים."];
        if (settings.Difficulty is not ("easy" or "medium" or "hard"))
            errors[$"{path}.difficulty"] = ["יש לבחור רמת קושי."];
        if (settings.QuestionCount is < 1 or > EngineValidation.MaxQuestionCount)
            errors[$"{path}.questionCount"] = [$"מספר השאלות חייב להיות בין 1 ל־{EngineValidation.MaxQuestionCount}."];
        return errors;
    }
}
