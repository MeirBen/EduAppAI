using FamilyLearning.Api.Features.Assignments;
using FamilyLearning.Api.TaskEngine.Models;

namespace FamilyLearning.Api.Tests.Assignments;

public sealed class SessionScoringTests
{
    [Fact]
    public void Parent_review_fills_only_pending_awards_without_mutating_or_recalculating_frozen_rows()
    {
        QuestionEvaluation[] rows = [new("auto", 5, 4, "automatic"), new("blank", 2, 0, "automatic"),
            new("previous", 3, 2, "parent"), new("text", 6, null, "parent"), new("zero", 0, null, "parent")];
        var frozen = new SessionEvaluation(rows, 4, 16, 2, null);
        var result = SessionScoring.CompleteReview(frozen, [new("zero", 0), new("text", 3)]);
        Assert.Equal(0, result.PendingCount);
        Assert.Equal(9, result.FinalTotal);
        Assert.Equal(4, result.AutomaticSubtotal);
        Assert.Equal(16, result.PossibleTotal);
        Assert.Equal(rows[..3], result.Questions[..3]);
        Assert.Equal(new QuestionEvaluation("text", 6, 3, "parent"), result.Questions[3]);
        Assert.Equal(new QuestionEvaluation("zero", 0, 0, "parent"), result.Questions[4]);
        Assert.Null(frozen.FinalTotal);
        Assert.Null(frozen.Questions[3].AwardedPoints);
        Assert.Equal(2, frozen.PendingCount);
    }

    internal static TaskDocument Document(string type, string key, int points = 5) => new("תרגול", null, [],
        [new("q1", "שאלה", new(type, type == "single-choice" ? ["כן", "לא"] : null), new(key), points, new("manual"), null)]);

    [Theory]
    [InlineData("single-choice", "כן", "כן", 5)]
    [InlineData("single-choice", "כן", "לא", 0)]
    [InlineData("numeric-input", "2", "+02.00", 5)]
    [InlineData("numeric-input", "-2.50", "-02.500", 5)]
    [InlineData("numeric-input", "-2.50", "2.50", 0)]
    [InlineData("numeric-input", "0", "-000.000", 5)]
    [InlineData("numeric-input", "+0.000", "-0", 5)]
    [InlineData("numeric-input", "1.123456789012345678901234567890", "01.12345678901234567890123456789", 5)]
    [InlineData("numeric-input", "1.123456789012345678901234567891", "1.123456789012345678901234567892", 0)]
    [InlineData("numeric-input", "1.123456789012345678901234567892", "1.123456789012345678901234567891", 0)]
    [InlineData("numeric-input", "0", "0.000000000000000000000000000001", 0)]
    [InlineData("numeric-input", "0.000000000000000000000000000001", "0", 0)]
    [InlineData("numeric-input", "0", "  ", 0)]
    [InlineData("text-input", "שָׁלוֹם", "\t ", 0)]
    public void Automatic_awards_use_exact_values_without_decimal_rounding(string type, string key, string answer, int expected)
    {
        var document = Document(type, key);
        SessionAnswer[] answers = [new("q1", answer)];
        Assert.Empty(SessionValidation.Validate(document, answers, submitting: true));
        var result = SessionScoring.Evaluate(document, answers);
        Assert.Equal(new QuestionEvaluation("q1", 5, expected, "automatic"), Assert.Single(result.Questions));
        Assert.Equal(expected, result.AutomaticSubtotal);
        Assert.Equal(5, result.PossibleTotal);
        Assert.Equal(0, result.PendingCount);
        Assert.Equal(expected, result.FinalTotal);
        Assert.Equal(answer, answers[0].Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Every_nonblank_text_answer_needs_parent_review_even_matching_keys_and_zero_points(int points)
    {
        const string answer = "  שָׁלוֹם\nעולם  ";
        SessionAnswer[] answers = [new("q1", answer)];
        var result = SessionScoring.Evaluate(Document("text-input", answer, points), answers);
        Assert.Equal(new QuestionEvaluation("q1", points, null, "parent"), Assert.Single(result.Questions));
        Assert.Equal(0, result.AutomaticSubtotal);
        Assert.Equal(points, result.PossibleTotal);
        Assert.Equal(1, result.PendingCount);
        Assert.Null(result.FinalTotal);
        Assert.Equal(answer, answers[0].Value);
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("1e2")]
    [InlineData("NaN")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("+")]
    [InlineData("1.")]
    [InlineData(" 2 ")]
    public void Numeric_grammar_is_enforced_at_submission_but_not_while_saving(string value)
    {
        var document = Document("numeric-input", "2");
        SessionAnswer[] answers = [new("q1", value)];
        Assert.Empty(SessionValidation.Validate(document, answers, submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, answers, submitting: true));
    }

    [Fact]
    public void Invalid_choice_and_oversized_blank_values_cannot_be_saved()
    {
        var document = Document("single-choice", "כן");
        Assert.NotEmpty(SessionValidation.Validate(document, [new("q1", "maybe")], submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, [new("q1", new string(' ', 201))], submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, null, submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, [null!], submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, [new(null!, "כן")], submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, [new("q1", null!)], submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, [new("unknown", "כן")], submitting: false));
        Assert.NotEmpty(SessionValidation.Validate(document, [new("q1", "כן"), new("q1", "לא")], submitting: false));
    }
}
