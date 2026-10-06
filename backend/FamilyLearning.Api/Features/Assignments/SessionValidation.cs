using FamilyLearning.Api.TaskEngine.Models;
using FamilyLearning.Api.TaskEngine.Validation;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Bounds raw answer buffers before save, scoring or replay comparison.</summary>
public static class SessionValidation
{
    /// <summary>Validates untrusted answers against a released document; incomplete numeric text is allowed only on save.</summary>
    public static Dictionary<string, string[]> Validate(TaskDocument document, SessionAnswer[]? answers, bool submitting)
    {
        var errors = new Dictionary<string, string[]>();
        if (answers is null || answers.Length > document.Questions.Length || answers.Length > EngineValidation.MaxQuestionCount)
        {
            errors["answers"] = ["יש לשלוח רשימת תשובות שאינה חורגת ממספר השאלות בפעילות."];
            return errors;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < answers.Length; index++)
        {
            var answer = answers[index];
            var field = $"answers[{index}]";
            if (answer is null || string.IsNullOrEmpty(answer.QuestionId) || answer.QuestionId.Length > 32 ||
                answer.Value is null || answer.Value.Length > EngineValidation.AnswerLength)
            {
                errors[field] = [$"יש לציין מזהה שאלה ותשובה באורך של עד {EngineValidation.AnswerLength} תווים."];
                continue;
            }
            var question = Array.Find(document.Questions, q => q.Id == answer.QuestionId);
            if (question is null || !ids.Add(answer.QuestionId))
            {
                errors[field] = ["מזהה השאלה אינו מוכר או מופיע יותר מפעם אחת."];
                continue;
            }
            // Raw bounds apply even to unanswered values; nonblank values are never trimmed.
            if (string.IsNullOrWhiteSpace(answer.Value)) continue;
            if (question.Interaction.Type == "single-choice" && !question.Interaction.Options!.Contains(answer.Value, StringComparer.Ordinal))
                errors[field] = ["יש לבחור אחת מהאפשרויות בפעילות."];
            else if (submitting && question.Interaction.Type == "numeric-input" && !QuestionRules.ValidNumericAnswer(answer.Value))
                errors[field] = ["יש להזין מספר רגיל, עם נקודה עשרונית לפי הצורך וללא מפרידי אלפים."];
        }
        return errors;
    }

    /// <summary>Compares validated buffers by ID, ignoring order and unanswered entries, without normalizing nonblank text.</summary>
    internal static bool SameAnswers(SessionAnswer[] left, SessionAnswer[] right)
    {
        var leftCount = 0;
        foreach (var answer in left)
        {
            if (string.IsNullOrWhiteSpace(answer.Value)) continue;
            leftCount++;
            if (!Array.Exists(right, other => other.QuestionId == answer.QuestionId && other.Value == answer.Value)) return false;
        }
        var rightCount = 0;
        foreach (var answer in right)
            if (!string.IsNullOrWhiteSpace(answer.Value)) rightCount++;
        return leftCount == rightCount;
    }
}
