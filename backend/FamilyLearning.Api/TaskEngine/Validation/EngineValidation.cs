namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Shared operational bounds and bounded field feedback for the content-first engine.</summary>
internal static class EngineValidation
{
    internal const int ContentLimit = 8000;
    internal const int BodyLimit = 4000;
    internal const int PlanLimit = 24000;
    // Strict provider schemas pin the exact question count; Gemini's schema budget accepted 20 for every valid shape.
    internal const int MaxQuestionCount = 20;

    internal static long MinimumQuestionLength(int count, string[] formats, int? choices, string? selectedFormat = null)
    {
        int choiceQuestions;
        if (selectedFormat is not null)
            choiceQuestions = selectedFormat == "single-choice" ? count : 0;
        else if (formats.Length == 1 && formats[0] == "single-choice")
            choiceQuestions = count;
        else
            choiceQuestions = formats.Contains("single-choice") ? 1 : 0;
        return checked(2L * count + (long)choiceQuestions * (choices ?? 0));
    }

    internal static bool HasText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    internal static bool IsId(string? id) => id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool IsFormat(string? format) => format is "numeric-input" or "text-input" or "single-choice";

    internal static void ValidateId(string? id, string path, HashSet<string> ids, Dictionary<string, string[]> errors)
    {
        if (!IsId(id) || !ids.Add(id!)) errors.AddError(path + ".id", "מזהה חייב להיות ייחודי ובמבנה נתמך.");
    }

    internal static void AddError(this Dictionary<string, string[]> errors, string path, string message)
    {
        if (errors.ContainsKey(path) || errors.Count < 99) errors[path] = [message];
        else errors["remainingErrors"] = ["יש לתקן שגיאות נוספות בתוכן."];
    }
}
