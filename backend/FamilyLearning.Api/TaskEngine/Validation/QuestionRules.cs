using System.Globalization;
using System.Text.RegularExpressions;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Answer and option rules shared by candidate, draft and release validation.</summary>
internal static partial class QuestionRules
{
    internal static bool ValidOptions(string[]? options) => options is { Length: >= 2 and <= 6 } &&
        options.All(option => EngineValidation.HasText(option, 200) && option == option.Trim() &&
            !option.Contains('\r') && !option.Contains('\n')) &&
        options.Distinct(StringComparer.Ordinal).Count() == options.Length;

    internal static bool ValidNumericAnswer(string value) => NumericAnswer().IsMatch(value) &&
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    internal static string? AnswerError(QuestionInteraction interaction, QuestionAnswer? answer)
    {
        if (answer is null || !EngineValidation.HasText(answer.Value, 200)) return "יש להזין תשובה באורך של 1 עד 200 תווים.";
        if (interaction.Type == "single-choice" && interaction.Options?.Count(o => o == answer.Value) != 1)
            return "התשובה הנכונה חייבת להיות אחת מהאפשרויות.";
        if (interaction.Type == "numeric-input" && !ValidNumericAnswer(answer.Value))
            return "יש להזין תשובה מספרית רגילה, עם נקודה עשרונית לפי הצורך וללא מפרידי אלפים.";
        return null;
    }

    [GeneratedRegex("\\A[+-]?[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumericAnswer();
}
