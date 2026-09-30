using System.Text.RegularExpressions;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.TaskEngine.Validation;

/// <summary>Bounds untrusted plain-text task content and validates its parent-only answer keys.</summary>
public static partial class TaskContentValidator
{
    /// <summary>Validates AI content before it is saved as an immutable task.</summary>
    /// <param name="content">Untrusted generated content.</param>
    /// <param name="expectedQuestionCount">Exact resolved count, or null to allow any positive count.</param>
    /// <remarks>Accepts potentially null nested JSON members. An empty result means the content is supported.</remarks>
    public static Dictionary<string, string[]> Validate(TaskContent? content, int? expectedQuestionCount = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (content is null)
        {
            errors["content"] = ["יש לציין את תוכן התרגול."];
            return errors;
        }
        if (!HasText(content.Title, 100)) errors["title"] = ["יש להזין כותרת באורך של 1 עד 100 תווים."];
        if (content.Instructions?.Length > 1000) errors["instructions"] = ["ההנחיות מוגבלות ל־1,000 תווים."];
        var length = (long)(content.Title?.Length ?? 0) + (content.Instructions?.Length ?? 0);
        if (content.ContentBlocks is not { Length: <= 4 })
            errors["contentBlocks"] = ["יש לציין רשימת קטעי קריאה, עד ארבעה קטעים."];
        else
            for (var index = 0; index < content.ContentBlocks.Length; index++)
            {
                var block = content.ContentBlocks[index];
                if (block is not { Type: "text" } || !HasText(block.Text, 4000))
                    errors[$"contentBlocks[{index}]"] = ["קטע קריאה חייב להכיל טקסט באורך של 1 עד 4,000 תווים."];
                length += block?.Text?.Length ?? 0;
            }

        if (content.Questions is not { Length: >= 1 })
            errors["questions"] = ["יש להוסיף לפחות שאלה אחת."];
        else
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < content.Questions.Length; index++)
            {
                var question = content.Questions[index];
                var error = ValidateQuestion(question, ids);
                if (error is not null) errors[$"questions[{index}]"] = [error];
                length += (long)(question?.Prompt?.Length ?? 0) + (question?.Answer?.Value?.Length ?? 0);
                if (question?.Interaction?.Options is { } options)
                    foreach (var option in options) length += option?.Length ?? 0;
            }
        }
        if (length > 8000) errors["content"] = ["התוכן כולו מוגבל ל־8,000 תווים, כולל שאלות ותשובות."];
        if (expectedQuestionCount.HasValue && content.Questions is { } questions && questions.Length != expectedQuestionCount.Value)
            errors["questions"] = ["מספר השאלות שנוצרו אינו תואם למספר שנבחר."];
        return errors;
    }

    private static string? ValidateQuestion(TaskQuestion? question, HashSet<string> ids)
    {
        if (question is null) return "שאלה אינה יכולה להיות ריקה.";
        if (question.Id is null || !QuestionId().IsMatch(question.Id) || !ids.Add(question.Id))
            return "מזהה השאלה חייב להיות ייחודי ולהכיל עד 64 אותיות לטיניות, ספרות, מקפים או קווים תחתונים.";
        if (!HasText(question.Prompt, 500)) return "יש להזין שאלה באורך של 1 עד 500 תווים.";
        if (question.Points is < 0 or > 100) return "הניקוד חייב להיות מספר שלם בין 0 ל־100.";
        if (question.Answer is null || !HasText(question.Answer.Value, 200))
            return "יש להזין תשובה באורך של 1 עד 200 תווים.";
        if (question.Interaction is not { Type: "numeric-input" or "text-input" or "single-choice" } interaction)
            return "יש לבחור סוג תשובה נתמך.";
        if (interaction.Type == "single-choice")
        {
            if (!QuestionRules.ValidOptions(interaction.Options))
                return "יש להזין בין שתיים לשש אפשרויות שונות, כל אחת בשורה אחת וללא רווחים בקצוות, עד 200 תווים.";
            if (!interaction.Options!.Contains(question.Answer.Value, StringComparer.Ordinal))
                return "התשובה הנכונה חייבת להיות אחת מהאפשרויות.";
        }
        else if (interaction.Options is not null) return "אפשרויות תשובה מתאימות רק לשאלת בחירה.";
        if (interaction.Type == "numeric-input" && !QuestionRules.ValidNumericAnswer(question.Answer.Value))
            return "יש להזין תשובה מספרית רגילה, עם נקודה עשרונית לפי הצורך וללא מפרידי אלפים.";
        return null;
    }

    private static bool HasText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    [GeneratedRegex("\\A[a-zA-Z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex QuestionId();
}
