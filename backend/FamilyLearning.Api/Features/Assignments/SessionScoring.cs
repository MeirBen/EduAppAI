using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Features.Assignments;

/// <summary>Deterministic submission scoring, independent of generation engine revisions.</summary>
public static class SessionScoring
{
    public const int PolicyVersion = 1;
    /// <summary>Completes only pending rows in a frozen evaluation. Grades must already match the pending IDs and point bounds.</summary>
    public static SessionEvaluation CompleteReview(SessionEvaluation evaluation, ParentGrade[] grades)
    {
        var rows = new QuestionEvaluation[evaluation.Questions.Length];
        var finalTotal = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = evaluation.Questions[index];
            if (row.GradingMethod == "parent" && row.AwardedPoints is null)
                row = row with { AwardedPoints = Array.Find(grades, g => g.QuestionId == row.QuestionId)!.Points };
            rows[index] = row;
            finalTotal += row.AwardedPoints!.Value;
        }
        return evaluation with { Questions = rows, PendingCount = 0, FinalTotal = finalTotal };
    }

    /// <summary>Evaluates a released document and answers that passed submission validation.</summary>
    public static SessionEvaluation Evaluate(TaskDocument document, SessionAnswer[] answers)
    {
        var rows = new QuestionEvaluation[document.Questions.Length];
        var automaticSubtotal = 0;
        var possibleTotal = 0;
        var pendingCount = 0;
        for (var index = 0; index < document.Questions.Length; index++)
        {
            var question = document.Questions[index];
            var value = Array.Find(answers, a => a.QuestionId == question.Id)?.Value;
            int? award = 0;
            var method = "automatic";
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (question.Interaction.Type == "text-input")
                {
                    award = null;
                    method = "parent";
                    pendingCount++;
                }
                else if (question.Interaction.Type == "numeric-input"
                    ? NumericEquals(value, question.Answer!.Value)
                    : string.Equals(value, question.Answer!.Value, StringComparison.Ordinal))
                    award = question.Points;
            }
            rows[index] = new(question.Id, question.Points, award, method);
            possibleTotal += question.Points;
            automaticSubtotal += award ?? 0;
        }
        return new(rows, automaticSubtotal, possibleTotal, pendingCount, pendingCount == 0 ? automaticSubtotal : null);
    }

    // Inputs passed the existing decimal grammar/range check. Parsing for equality would round long fractions.
    private static bool NumericEquals(string left, string right)
    {
        NormalizeNumber(left, out var leftNegative, out var leftInteger, out var leftFraction);
        NormalizeNumber(right, out var rightNegative, out var rightInteger, out var rightFraction);
        return leftNegative == rightNegative && leftInteger.SequenceEqual(rightInteger) && leftFraction.SequenceEqual(rightFraction);
    }

    private static void NormalizeNumber(ReadOnlySpan<char> value, out bool negative,
        out ReadOnlySpan<char> integer, out ReadOnlySpan<char> fraction)
    {
        negative = value[0] == '-';
        if (value[0] is '+' or '-') value = value[1..];
        var point = value.IndexOf('.');
        integer = point < 0 ? value : value[..point];
        fraction = point < 0 ? [] : value[(point + 1)..];
        while (!integer.IsEmpty && integer[0] == '0') integer = integer[1..];
        while (!fraction.IsEmpty && fraction[^1] == '0') fraction = fraction[..^1];
        if (integer.IsEmpty && fraction.IsEmpty) negative = false;
    }
}
