using System.Globalization;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Shared operational bounds and bounded field feedback for the content-first engine.</summary>
internal static class EngineValidation
{
    internal const int ContentLimit = 8000;
    internal const int BodyLimit = 4000;
    internal const int PlanLimit = 24000;
    // Product caps. Strict schemas carry exact question counts up to Ai:StrictQuestionCountLimit, choice counts always.
    internal const int MaxQuestionCount = 20;
    internal const int MinChoiceCount = 2;
    internal const int MaxChoiceCount = 6;
    // Stated in the authoring prompt, not as schema bounds: bounded control and option lists exceed provider schema budgets.
    internal const int MaxControls = 16;
    internal const int MaxSelectOptions = 20;
    internal const int MaxMaterials = 4;
    internal const int MaxPoints = 100;
    // Field lengths; ContentLimits publishes them so the client mirrors exactly what these validators enforce.
    internal const int NameLength = 100;
    internal const int GoalLength = 500;
    internal const int GuidanceLength = 4000;
    internal const int ScopedGuidanceLength = 1000;
    internal const int MeaningLength = 500;
    internal const int TextValueLength = 500;
    internal const int SelectOptionLength = 100;
    internal const int SelectOptionMeaningLength = 200;
    internal const int SettingTextLength = 200;
    internal const int TitleLength = 100;
    internal const int InstructionsLength = 1000;
    internal const int PromptLength = 500;
    internal const int AnswerLength = 200;
    internal const int MessageLength = 4000;
    internal const int MaxContextTurns = 6;
    internal const int ContextLength = 12000;
    internal const int ListLimit = 100;

    internal static readonly string ContentLimitError = $"התוכן כולו מוגבל ל־{Count(ContentLimit)} תווים.";

    /// <summary>Formats a limit for Hebrew feedback with invariant digit grouping, independent of server culture.</summary>
    internal static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

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
