using FamilyLearning.Evaluation;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ExactArithmeticTests
{
    [Theory]
    [InlineData("125 × 6 =", "750")]
    [InlineData("488 : 4 =", "122")]
    [InlineData("(15 + 9) : 4 + 7 × 3", "27")]
    [InlineData("50 - (18 - 6) : 3 × 4", "34")]
    [InlineData("12 − 5 = ?", "7")]
    [InlineData("3.4 + 2.15 =", "5.55")]
    [InlineData("3.4 + 2.15 =", "5.550")]
    [InlineData("7.2 - 3.45 =", "03.75")]
    [InlineData("1/4 + 1/6 =", "5/12")]
    [InlineData("7/8 - 1/2 =", "3/8")]
    [InlineData("2/6 + 1/3", "2/3")]
    [InlineData("-3 + 8 =", "5")]
    [InlineData("2 · 3 ÷ 4", "1.5")]
    public void Calculation_prompts_and_keys_compare_as_exact_values(string prompt, string key)
    {
        Assert.True(ExactArithmetic.TryEvaluate(prompt, out var expected));
        Assert.True(ExactArithmetic.TryEvaluate(key, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("1/3 + 1/6 =", "0.5", true)]
    [InlineData("1/3 =", "0.33", false)]
    [InlineData("48 × 7 =", "335", false)]
    [InlineData("2/6 + 1/3", "4/6", true)]
    public void Equality_is_by_value_not_spelling(string prompt, string key, bool equal)
    {
        Assert.True(ExactArithmetic.TryEvaluate(prompt, out var expected));
        Assert.True(ExactArithmetic.TryEvaluate(key, out var actual));
        Assert.Equal(equal, expected == actual);
    }

    [Theory]
    [InlineData("3/7 ___ 5/7")]
    [InlineData("כמה הם 1+1?")]
    [InlineData("43 לחלק ל-5")]
    [InlineData("480 : 0 =")]
    [InlineData("12 + =")]
    [InlineData("(3 + 4")]
    [InlineData("1,000 + 1")]
    [InlineData("3. + 1")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_text_is_not_a_calculation(string? text) => Assert.False(ExactArithmetic.TryEvaluate(text, out _));
}
